namespace Tracer.Api.Middleware;

/// <summary>
/// Configuration for <see cref="SecurityHeadersMiddleware"/>. Bound from the
/// <c>Security:Headers</c> configuration section.
/// </summary>
/// <remarks>
/// HSTS header values are intentionally applied by the dedicated
/// <c>app.UseHsts()</c> built-in middleware (production only), not by this
/// middleware — this class tracks only the values that feed into
/// <c>HstsOptions</c>. The other fields (CSP, Permissions-Policy, etc.) are
/// written on every response by <see cref="SecurityHeadersMiddleware"/>.
/// </remarks>
internal sealed class SecurityHeadersOptions
{
    public const string SectionName = "Security:Headers";

    /// <summary>
    /// Master switch. When <c>false</c>, the middleware is still registered
    /// but does not set any headers. Useful for local debugging, never in
    /// production.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// HSTS max-age in seconds. RFC 6797 recommends at least six months for
    /// preload eligibility; we default to two years (63 072 000 s).
    /// </summary>
    public int HstsMaxAgeSeconds { get; init; } = 63_072_000;

    public bool HstsIncludeSubDomains { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, appends <c>; preload</c> to the HSTS directive. Only
    /// enable after the domain is actually submitted to the preload list
    /// (hstspreload.org).
    /// </summary>
    public bool HstsPreload { get; init; }

    /// <summary>
    /// Content-Security-Policy header value. Default denies every source —
    /// Tracer API responses are JSON / problem+json only and must never be
    /// interpreted as HTML by a browser.
    /// </summary>
    public string ContentSecurityPolicy { get; init; } =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    public string ReferrerPolicy { get; init; } = "no-referrer";

    /// <summary>
    /// Permissions-Policy header. Default disables browser-level features
    /// (geolocation, camera, microphone, etc.) since the API is not a
    /// user-facing site.
    /// </summary>
    public string PermissionsPolicy { get; init; } =
        "accelerometer=(), autoplay=(), camera=(), encrypted-media=(), " +
        "fullscreen=(), geolocation=(), gyroscope=(), magnetometer=(), " +
        "microphone=(), midi=(), payment=(), picture-in-picture=(), " +
        "sync-xhr=(), usb=()";

    public string CrossOriginOpenerPolicy { get; init; } = "same-origin";

    public string CrossOriginResourcePolicy { get; init; } = "same-origin";

    /// <summary>
    /// X-Frame-Options value. DENY is strictest; change to SAMEORIGIN only if
    /// an internal tool needs to embed API responses, which should never
    /// happen for a JSON API.
    /// </summary>
    public string XFrameOptions { get; init; } = "DENY";
}
