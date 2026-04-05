namespace Tracer.Api.Middleware;

/// <summary>
/// Middleware that validates the <c>X-Api-Key</c> header against configured API keys.
/// Skips validation for health check and OpenAPI endpoints.
/// </summary>
internal sealed class ApiKeyAuthMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
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

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey) ||
            string.IsNullOrWhiteSpace(providedKey) ||
            !_validKeys.Contains(providedKey.ToString()))
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
