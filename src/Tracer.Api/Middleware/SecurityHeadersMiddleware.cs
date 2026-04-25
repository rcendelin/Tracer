using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Tracer.Api.Middleware;

/// <summary>
/// Writes a conservative set of security response headers on every request.
/// Headers are added before the response starts via <c>OnStarting</c>, so
/// downstream middleware cannot forget to include them on error paths.
/// </summary>
/// <remarks>
/// HSTS is emitted by the framework's <c>app.UseHsts()</c> middleware (prod
/// only). All other headers — CSP, Referrer-Policy, Permissions-Policy,
/// X-Content-Type-Options, X-Frame-Options, Cross-Origin-*-Policy — are
/// written here unconditionally unless <see cref="SecurityHeadersOptions.Enabled"/>
/// is false.
/// </remarks>
internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeadersOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
            return _next(context);

        context.Response.OnStarting(ApplyHeaders, context);
        return _next(context);
    }

    private Task ApplyHeaders(object state)
    {
        var context = (HttpContext)state;
        var headers = context.Response.Headers;

        SetIfMissing(headers, "Content-Security-Policy", _options.ContentSecurityPolicy);
        SetIfMissing(headers, "Referrer-Policy", _options.ReferrerPolicy);
        SetIfMissing(headers, "Permissions-Policy", _options.PermissionsPolicy);
        SetIfMissing(headers, "Cross-Origin-Opener-Policy", _options.CrossOriginOpenerPolicy);
        SetIfMissing(headers, "Cross-Origin-Resource-Policy", _options.CrossOriginResourcePolicy);
        SetIfMissing(headers, "X-Content-Type-Options", "nosniff");
        SetIfMissing(headers, "X-Frame-Options", _options.XFrameOptions);

        // Leak-prevention: remove the framework's Server header if present.
        // ASP.NET Core doesn't add "Server: Kestrel" by default, but Front Door
        // / App Service sometimes inject one. Removing it reduces fingerprint.
        headers.Remove(HeaderNames.Server);

        return Task.CompletedTask;
    }

    private static void SetIfMissing(IHeaderDictionary headers, string name, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        // Don't overwrite values set explicitly by an endpoint (e.g. a custom
        // CSP on the OpenAPI page served by Scalar).
        if (headers.ContainsKey(name))
            return;

        headers[name] = value;
    }
}

internal static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
