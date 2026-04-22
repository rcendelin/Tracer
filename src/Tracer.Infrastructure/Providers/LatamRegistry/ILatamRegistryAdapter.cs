namespace Tracer.Infrastructure.Providers.LatamRegistry;

/// <summary>
/// Strategy interface for per-country LATAM registry adapters.
/// Each adapter owns the registry-specific URL, request shape, identifier format
/// (CUIT / RUT / NIT / RFC) and the HTML/JSON parser that produces a normalized
/// <see cref="LatamRegistrySearchResult"/>.
/// <para>
/// The <see cref="LatamRegistryClient"/> dispatches lookups to the adapter whose
/// <see cref="CountryCode"/> matches the request. All adapters share the same
/// rate-limit window and SSRF guard in the client.
/// </para>
/// </summary>
internal interface ILatamRegistryAdapter
{
    /// <summary>Gets the ISO-3166-1 alpha-2 country code (e.g. "AR", "CL", "CO", "MX").</summary>
    string CountryCode { get; }

    /// <summary>
    /// Gets the base URL of the registry portal. Used by the client both for
    /// building the absolute lookup URL and for the SSRF DNS check.
    /// </summary>
    string BaseUrl { get; }

    /// <summary>
    /// Normalizes an identifier to the form accepted by the registry (e.g. strips
    /// formatting dashes from a CUIT) and validates it; returns <see langword="null"/>
    /// if the identifier does not match the country's expected format.
    /// </summary>
    string? NormalizeIdentifier(string identifier);

    /// <summary>
    /// Builds the HTTP request for a lookup by normalized identifier.
    /// The resulting request has a fully-qualified <see cref="HttpRequestMessage.RequestUri"/>
    /// on the registry's host so <see cref="LatamRegistryClient"/> can apply the SSRF
    /// guard before sending.
    /// </summary>
    HttpRequestMessage BuildLookupRequest(string normalizedIdentifier);

    /// <summary>
    /// Parses the registry's response body into a <see cref="LatamRegistrySearchResult"/>.
    /// Returns <see langword="null"/> when the registry did not return a match
    /// (including CAPTCHA / login-wall pages — parsing failures are not errors).
    /// </summary>
    LatamRegistrySearchResult? Parse(string body, string normalizedIdentifier);
}
