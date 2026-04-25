namespace Tracer.Contracts.Enums;

/// <summary>
/// Identifies a specific enriched field on a company profile.
/// Carried in <see cref="Messages.ChangeEventMessage"/> to indicate which field changed.
/// </summary>
public enum FieldName
{
    /// <summary>Official registered legal name of the company.</summary>
    LegalName = 0,

    /// <summary>Trading name / DBA name (may differ from legal name).</summary>
    TradeName = 1,

    /// <summary>Company registration number (IČO, CRN, ABN, CIK, etc.).</summary>
    RegistrationId = 2,

    /// <summary>Tax identification number (DIČ, VAT ID, EIN, etc.).</summary>
    TaxId = 3,

    /// <summary>Legal entity form (s.r.o., a.s., Ltd., GmbH, etc.).</summary>
    LegalForm = 4,

    /// <summary>Registered (statutory) address.</summary>
    RegisteredAddress = 5,

    /// <summary>Actual operating / mailing address (may differ from registered).</summary>
    OperatingAddress = 6,

    /// <summary>Primary business phone number.</summary>
    Phone = 7,

    /// <summary>Primary business email address.</summary>
    Email = 8,

    /// <summary>Company website URL.</summary>
    Website = 9,

    /// <summary>Primary industry classification (NACE/SIC/NAICS description).</summary>
    Industry = 10,

    /// <summary>Employee count range (e.g. "50-99", "100-249").</summary>
    EmployeeRange = 11,

    /// <summary>
    /// Entity status (Active, Dissolved, In Liquidation, etc.).
    /// Critical changes on this field trigger immediate Service Bus notifications.
    /// </summary>
    EntityStatus = 12,

    /// <summary>Parent / owning company (if known).</summary>
    ParentCompany = 13,

    /// <summary>GPS coordinates of the primary address.</summary>
    Location = 14,

    /// <summary>
    /// Directors / key officers.
    /// GDPR-gated: only enriched when the calling tenant has opted in to personal data processing.
    /// </summary>
    Officers = 15,
}
