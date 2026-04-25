using Tracer.Domain.Enums;

namespace Tracer.Application.DTOs;

/// <summary>
/// Input DTO for submitting an enrichment request via the API.
/// </summary>
public sealed record TraceRequestDto
{
    /// <summary>
    /// Optional caller-supplied correlation ID. Echoed back in batch responses for request-reply matching.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>Company name hint (legal or trade name). Either this or <see cref="RegistrationId"/> must be present.</summary>
    public string? CompanyName { get; init; }

    /// <summary>Phone number in any format; providers normalize to E.164 internally.</summary>
    public string? Phone { get; init; }

    /// <summary>Contact e-mail address.</summary>
    public string? Email { get; init; }

    /// <summary>Company website (scraped by Tier 2 providers at Standard depth or higher).</summary>
    public string? Website { get; init; }

    /// <summary>Street / postal address line.</summary>
    public string? Address { get; init; }

    /// <summary>City / locality.</summary>
    public string? City { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code (e.g. <c>"CZ"</c>, <c>"DE"</c>).</summary>
    public string? Country { get; init; }

    /// <summary>Official registration ID (IČO, CRN, CNPJ, ABN, CIK, filing number). Format depends on jurisdiction.</summary>
    public string? RegistrationId { get; init; }

    /// <summary>Tax / VAT ID.</summary>
    public string? TaxId { get; init; }

    /// <summary>Free-text industry hint used by the AI extractor (Deep depth only).</summary>
    public string? IndustryHint { get; init; }

    /// <summary>Waterfall depth: <c>Quick</c> (≤5 s, Tier 1 APIs), <c>Standard</c> (≤15 s, + scraping), <c>Deep</c> (≤30 s, + AI extraction).</summary>
    public TraceDepth Depth { get; init; } = TraceDepth.Standard;

    /// <summary>
    /// Optional HTTPS webhook invoked when the trace finishes. See
    /// <c>IWebhookCallbackService</c> for retry / signing semantics.
    /// </summary>
    public Uri? CallbackUrl { get; init; }
}
