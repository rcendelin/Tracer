namespace Tracer.Infrastructure.Providers.LatamRegistry;

/// <summary>
/// HTTP client abstraction for LATAM registry lookups.
/// Dispatches to per-country <see cref="ILatamRegistryAdapter"/> implementations
/// based on the supplied country code and enforces a shared rate limit + SSRF guard.
/// </summary>
internal interface ILatamRegistryClient
{
    /// <summary>
    /// Looks up a company by tax identifier in the registry matching
    /// <paramref name="countryCode"/>.
    /// </summary>
    /// <param name="countryCode">
    /// ISO-3166-1 alpha-2 code — "AR" | "CL" | "CO" | "MX". Case-insensitive.
    /// </param>
    /// <param name="identifier">
    /// Tax identifier — CUIT (AR), RUT (CL), NIT (CO) or RFC (MX). May be formatted
    /// (dashes / dots); the adapter normalizes it before sending the request.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The parsed registry result, or <see langword="null"/> if no adapter matches
    /// the country, the identifier fails validation, the SSRF guard blocks the
    /// request, the registry returned a non-success response, or the parser could
    /// not extract a match (including CAPTCHA / login walls).
    /// </returns>
    Task<LatamRegistrySearchResult?> LookupAsync(
        string countryCode,
        string identifier,
        CancellationToken ct);
}
