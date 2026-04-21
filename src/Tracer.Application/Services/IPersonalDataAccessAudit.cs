using Tracer.Domain.Enums;

namespace Tracer.Application.Services;

/// <summary>
/// Records read access to personal-data fields for GDPR Art. 30 compliance
/// (records of processing activities).
/// </summary>
/// <remarks>
/// <para>
/// The audit hook is intentionally decoupled from any specific storage
/// implementation. The default <see cref="LoggingPersonalDataAccessAudit"/>
/// writes structured log entries consumable by any log sink (Serilog /
/// Application Insights). A future implementation can persist to a dedicated
/// audit table without changing call sites.
/// </para>
/// <para>
/// Callers are expected to invoke <see cref="RecordAccess"/> exactly once per
/// read of a personal-data field (e.g. in the profile detail endpoint when
/// serialising <c>Officers</c>). Implementation is Singleton and thread-safe.
/// </para>
/// </remarks>
public interface IPersonalDataAccessAudit
{
    /// <summary>
    /// Records that <paramref name="accessor"/> read personal data
    /// on the given <paramref name="profileId"/> / <paramref name="field"/>.
    /// </summary>
    /// <param name="profileId">CKB profile identifier.</param>
    /// <param name="field">The personal-data field that was read.</param>
    /// <param name="accessor">
    /// Stable identifier of the caller — API key name, user principal, or
    /// background service name. Non-null, non-empty.
    /// </param>
    /// <param name="purpose">
    /// Short, free-form description of why the data was accessed
    /// (e.g. <c>"profile-detail-endpoint"</c>). Non-null, non-empty.
    /// </param>
    void RecordAccess(Guid profileId, FieldName field, string accessor, string purpose);
}
