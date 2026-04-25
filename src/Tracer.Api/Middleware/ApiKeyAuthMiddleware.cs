using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Tracer.Api.Middleware;

/// <summary>
/// Middleware that validates the <c>X-Api-Key</c> header against configured API keys.
/// Skips validation for health check and OpenAPI endpoints.
/// </summary>
/// <remarks>
/// WebSocket/SignalR support: SignalR cannot send custom HTTP headers during the
/// WebSocket upgrade, so the client passes the key via the SignalR
/// <c>accessTokenFactory</c> which sends it as:
/// <list type="bullet">
///   <item>An <c>Authorization: Bearer &lt;key&gt;</c> header for HTTP long-polling</item>
///   <item>An <c>access_token</c> query string parameter for WebSocket upgrades</item>
/// </list>
/// This middleware accepts the key from all three sources (X-Api-Key, Bearer, query string).
///
/// Rotation: keys carry optional <c>ExpiresAt</c> metadata. Expired keys are
/// silently ignored so an operator can pre-configure the next key ahead of
/// cut-over. A successful auth exposes the caller's fingerprint + label via
/// <c>HttpContext.Items</c> for downstream audit (see B-70 / B-85).
/// </remarks>
internal sealed class ApiKeyAuthMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string BearerPrefix = "Bearer ";
    private const string AccessTokenQueryParam = "access_token";

    /// <summary>Item key used to expose the caller fingerprint (SHA-256 prefix) to downstream handlers.</summary>
    public const string CallerFingerprintItemKey = "ApiKeyFingerprint";

    /// <summary>Item key used to expose the human-readable label of the matched key.</summary>
    public const string CallerLabelItemKey = "AuthLabel";

    private readonly RequestDelegate _next;
    private readonly ApiKeyOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly bool _isDevelopment;

    private static readonly HashSet<string> AnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
    };

    public ApiKeyAuthMiddleware(
        RequestDelegate next,
        IOptions<ApiKeyOptions> options,
        TimeProvider timeProvider,
        IWebHostEnvironment env)
    {
        _next = next;
        _options = options.Value;
        _timeProvider = timeProvider;
        _isDevelopment = env.IsDevelopment();

        if (_options.ApiKeys.Count == 0 && !_isDevelopment)
            throw new InvalidOperationException("Auth:ApiKeys must be configured in non-Development environments.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip auth for anonymous endpoints (exact match or segment boundary)
        if (AnonymousPaths.Contains(path) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Skip auth if no keys are configured (development only — enforced in constructor)
        if (_options.ApiKeys.Count == 0)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var presented = ExtractApiKey(context);
        var matched = TryMatch(presented);

        if (matched is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7235#section-3.1",
                title = "Unauthorized",
                status = 401,
                detail = "Missing or invalid API key. Provide a valid X-Api-Key header.",
            }).ConfigureAwait(false);
            return;
        }

        // Server-derived caller identity for audit (never trust request body).
        // Fingerprint is SHA-256 prefix — truncated to 8 hex chars so logs stay readable.
        context.Items[CallerFingerprintItemKey] = BuildFingerprint(matched.Key);
        context.Items[CallerLabelItemKey] = matched.Label ?? "unlabelled";

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds a non-expired entry matching the presented key using a
    /// constant-time byte comparison. Length-only short-circuit is
    /// intentional: configured key lengths are not secret.
    /// </summary>
    private ApiKeyEntry? TryMatch(string? presented)
    {
        if (string.IsNullOrWhiteSpace(presented))
            return null;

        var now = _timeProvider.GetUtcNow();
        var presentedBytes = Encoding.UTF8.GetBytes(presented);

        foreach (var entry in _options.ApiKeys)
        {
            if (!entry.IsActive(now))
                continue;

            var entryBytes = Encoding.UTF8.GetBytes(entry.Key);
            if (entryBytes.Length == presentedBytes.Length &&
                CryptographicOperations.FixedTimeEquals(presentedBytes, entryBytes))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the API key from (in order of preference):
    /// 1. <c>X-Api-Key</c> header — standard REST calls
    /// 2. <c>Authorization: Bearer &lt;key&gt;</c> header — SignalR long-polling
    /// 3. <c>access_token</c> query string — SignalR WebSocket upgrade
    /// </summary>
    private static string? ExtractApiKey(HttpContext context)
    {
        // 1. X-Api-Key header (primary path for REST clients)
        if (context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerKey) &&
            !string.IsNullOrWhiteSpace(headerKey))
        {
            return headerKey.ToString();
        }

        // 2. Authorization: Bearer <key> (SignalR long-polling / SSE)
        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var auth = authHeader.ToString();
            if (auth.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
                return auth[BearerPrefix.Length..].Trim();
        }

        // 3. access_token query string (SignalR WebSocket upgrade — browser WebSocket API
        //    cannot set custom headers, so the SignalR client sends the token here)
        if (context.Request.Query.TryGetValue(AccessTokenQueryParam, out var queryToken) &&
            !string.IsNullOrWhiteSpace(queryToken))
        {
            return queryToken.ToString();
        }

        return null;
    }

    private static string BuildFingerprint(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        // 8 hex chars keeps audit logs compact while avoiding meaningful collision risk.
        return "apikey:" + Convert.ToHexString(bytes.AsSpan(0, 4)).ToLowerInvariant();
    }
}

/// <summary>
/// Extension methods for registering the API key auth middleware.
/// </summary>
internal static class ApiKeyAuthMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyAuth(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ApiKeyAuthMiddleware>();
    }
}
