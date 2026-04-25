using Tracer.Contracts.Enums;

namespace Tracer.Contracts.Messages;

/// <summary>
/// Message sent by FieldForce (or any upstream service) to request company enrichment.
/// </summary>
/// <remarks>
/// <para><strong>Transport:</strong> Azure Service Bus queue <c>tracer-request</c>.</para>
/// <para><strong>Serialisation:</strong> JSON (camelCase), UTF-8, Content-Type: <c>application/json</c>.</para>
/// <para><strong>Request-reply:</strong> Set <see cref="CorrelationId"/> to your internal request ID.
/// Tracer echoes it back in <see cref="TraceResponseMessage.CorrelationId"/> so you can match
/// responses to requests on the <c>tracer-response</c> queue.</para>
/// <para><strong>At least one identifier required:</strong> Provide at minimum one of
/// <see cref="CompanyName"/>, <see cref="RegistrationId"/>, <see cref="Phone"/>,
/// <see cref="Email"/>, or <see cref="Website"/>. Richer input = higher match accuracy.</para>
/// </remarks>
/// <example>
/// Minimal request (company name + country):
/// <code>
/// {
///   "correlationId": "ff-req-00042",
///   "companyName": "ACME s.r.o.",
///   "country": "CZ",
///   "depth": 1
/// }
/// </code>
/// Rich request (with registration ID):
/// <code>
/// {
///   "correlationId": "ff-req-00043",
///   "companyName": "Škoda Auto a.s.",
///   "registrationId": "00177041",
///   "country": "CZ",
///   "depth": 1,
///   "source": "fieldforce-crm"
/// }
/// </code>
/// </example>
public sealed record TraceRequestMessage
{
    /// <summary>
    /// Your internal correlation ID for the request-reply pattern.
    /// Echoed back unchanged in <see cref="TraceResponseMessage.CorrelationId"/>.
    /// Use a stable, unique identifier (e.g. FieldForce account ID or a GUID).
    /// Maximum 256 characters.
    /// </summary>
    public required string CorrelationId { get; init; }

    // ── Company identifiers ───────────────────────────────────────────────────

    /// <summary>
    /// Full or partial company name. Used for fuzzy matching when no <see cref="RegistrationId"/> is provided.
    /// Maximum 500 characters.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// National company registration number (IČO for CZ/SK, CRN for UK, ABN for AU, CIK for US, etc.).
    /// Providing this enables direct registry lookup and maximises match accuracy.
    /// </summary>
    public string? RegistrationId { get; init; }

    /// <summary>Tax identification number (DIČ, VAT ID, EIN, etc.).</summary>
    public string? TaxId { get; init; }

    // ── Contact / location hints ──────────────────────────────────────────────

    /// <summary>Phone number in any format. Used as a secondary match signal.</summary>
    public string? Phone { get; init; }

    /// <summary>Business email address. Used as a secondary match signal.</summary>
    public string? Email { get; init; }

    /// <summary>Company website URL. Used as a secondary match signal and for web scraping.</summary>
    public string? Website { get; init; }

    /// <summary>Street address (partial is acceptable).</summary>
    public string? Address { get; init; }

    /// <summary>City / municipality.</summary>
    public string? City { get; init; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country code (e.g. <c>"CZ"</c>, <c>"GB"</c>, <c>"US"</c>).
    /// Strongly recommended — narrows provider selection and improves match accuracy.
    /// </summary>
    public string? Country { get; init; }

    /// <summary>
    /// Industry hint (free text, e.g. "automotive", "agriculture", "manufacturing").
    /// Used by AI extraction to resolve ambiguous company names.
    /// </summary>
    public string? IndustryHint { get; init; }

    // ── Request options ───────────────────────────────────────────────────────

    /// <summary>
    /// How deeply to enrich the profile.
    /// Default: <see cref="TraceDepth.Standard"/> (all Tier 1 API sources, &lt;10s).
    /// </summary>
    public TraceDepth Depth { get; init; } = TraceDepth.Standard;

    /// <summary>
    /// Identifies the caller for logging and rate-limit attribution.
    /// Set to a stable identifier such as <c>"fieldforce-crm"</c> or <c>"fieldforce-mobile"</c>.
    /// </summary>
    public string Source { get; init; } = "fieldforce";
}
