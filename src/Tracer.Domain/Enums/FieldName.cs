namespace Tracer.Domain.Enums;

/// <summary>
/// Identifies a specific enriched field on a <see cref="Entities.CompanyProfile"/>.
/// Used as a strongly-typed key in provider results, change events, and TTL policies.
/// </summary>
public enum FieldName
{
    LegalName = 0,
    TradeName = 1,
    RegistrationId = 2,
    TaxId = 3,
    LegalForm = 4,
    RegisteredAddress = 5,
    OperatingAddress = 6,
    Phone = 7,
    Email = 8,
    Website = 9,
    Industry = 10,
    EmployeeRange = 11,
    EntityStatus = 12,
    ParentCompany = 13,
    Location = 14,

    /// <summary>
    /// Directors / officers. GDPR-gated — only enriched when the calling tenant
    /// has opted in to personal data processing.
    /// </summary>
    Officers = 15,
}
