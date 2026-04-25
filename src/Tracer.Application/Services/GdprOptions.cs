namespace Tracer.Application.Services;

/// <summary>
/// Configuration for the GDPR classification and retention policy.
/// Bound from the <c>Gdpr</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class GdprOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Gdpr";

    /// <summary>
    /// Retention window for personal-data fields, in days. Default 1095 (≈36 months)
    /// following the platform baseline documented in <c>CLAUDE.md</c>. The retention
    /// job (B-70) soft-deletes personal-data fields whose <c>EnrichedAt</c> is older
    /// than this value.
    /// </summary>
    /// <remarks>Must be strictly positive; validated at DI time.</remarks>
    public int PersonalDataRetentionDays { get; init; } = 1095;

    /// <summary>
    /// When <c>true</c>, every read of a personal-data field emits an audit log
    /// entry via <see cref="IPersonalDataAccessAudit"/>. Default <c>true</c>. Turn
    /// off only for local development — production must keep this enabled to
    /// satisfy GDPR Art. 30 (records of processing).
    /// </summary>
    public bool AuditPersonalDataAccess { get; init; } = true;
}
