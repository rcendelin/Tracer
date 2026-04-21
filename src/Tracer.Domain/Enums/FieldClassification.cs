namespace Tracer.Domain.Enums;

/// <summary>
/// GDPR classification of a <see cref="FieldName"/>. Determines whether the
/// field contains data relating to an identified natural person and therefore
/// falls under the personal-data regime (opt-in, retention, audit logging).
/// </summary>
public enum FieldClassification
{
    /// <summary>
    /// Business / company data. No GDPR restrictions apply — fields can be
    /// enriched, stored and returned without explicit consent.
    /// Examples: legal name, registration ID, registered address, industry.
    /// </summary>
    Firmographic = 0,

    /// <summary>
    /// Data relating to an identified natural person in the sense of
    /// GDPR Art. 4(1). Enrichment requires opt-in, access must be audited,
    /// and the value is subject to the platform retention policy.
    /// Example: <see cref="FieldName.Officers"/> (directors / statutory bodies).
    /// </summary>
    PersonalData = 1,
}
