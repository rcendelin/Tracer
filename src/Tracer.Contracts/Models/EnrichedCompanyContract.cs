namespace Tracer.Contracts.Models;

/// <summary>
/// All enriched fields for a company, each with its own confidence score and source.
/// </summary>
/// <remarks>
/// Fields that could not be enriched are <see langword="null"/>.
/// Always check <see cref="TracedFieldContract{T}.Confidence"/> before writing a value
/// directly to the FieldForce CRM — low-confidence values may need human review.
/// </remarks>
public sealed record EnrichedCompanyContract
{
    /// <summary>Official registered legal name.</summary>
    public TracedFieldContract<string>? LegalName { get; init; }

    /// <summary>Trading / DBA name (may differ from legal name).</summary>
    public TracedFieldContract<string>? TradeName { get; init; }

    /// <summary>Tax identification number (DIČ, VAT ID, EIN, etc.).</summary>
    public TracedFieldContract<string>? TaxId { get; init; }

    /// <summary>Legal entity form (s.r.o., a.s., Ltd., GmbH, etc.).</summary>
    public TracedFieldContract<string>? LegalForm { get; init; }

    /// <summary>Registered (statutory) address.</summary>
    public TracedFieldContract<AddressContract>? RegisteredAddress { get; init; }

    /// <summary>Actual operating / mailing address.</summary>
    public TracedFieldContract<AddressContract>? OperatingAddress { get; init; }

    /// <summary>Primary business phone number (E.164 format where available).</summary>
    public TracedFieldContract<string>? Phone { get; init; }

    /// <summary>Primary business email address.</summary>
    public TracedFieldContract<string>? Email { get; init; }

    /// <summary>Company website URL.</summary>
    public TracedFieldContract<string>? Website { get; init; }

    /// <summary>Primary industry classification (NACE/SIC/NAICS description).</summary>
    public TracedFieldContract<string>? Industry { get; init; }

    /// <summary>Employee count range (e.g. "50-99", "100-249").</summary>
    public TracedFieldContract<string>? EmployeeRange { get; init; }

    /// <summary>
    /// Entity lifecycle status (e.g. "Active", "Dissolved", "In Liquidation").
    /// Critical changes to this field trigger immediate Service Bus notifications.
    /// </summary>
    public TracedFieldContract<string>? EntityStatus { get; init; }

    /// <summary>Parent / owning company name (if known).</summary>
    public TracedFieldContract<string>? ParentCompany { get; init; }

    /// <summary>GPS coordinates of the primary address.</summary>
    public TracedFieldContract<GeoCoordinateContract>? Location { get; init; }
}
