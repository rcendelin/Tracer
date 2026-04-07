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
/// </remarks>
internal sealed class ApiKeyAuthMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private const string BearerPrefix = "Bearer ";
    private const string AccessTokenQueryParam = "access_token";

    private readonly RequestDelegate _next;
    private readonly HashSet<string> _validKeys;
    private readonly bool _isDevelopment;

    private static readonly HashSet<string> AnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
    };

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment env)
    {
        _next = next;
        _isDevelopment = env.IsDevelopment();
        var keys = configuration.GetSection("Auth:ApiKeys").Get<string[]>() ?? [];
        _validKeys = new HashSet<string>(keys, StringComparer.Ordinal);

        if (_validKeys.Count == 0 && !_isDevelopment)
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
        if (_validKeys.Count == 0)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var key = ExtractApiKey(context);

        if (string.IsNullOrWhiteSpace(key) || !_validKeys.Contains(key))
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

        await _next(context).ConfigureAwait(false);
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
