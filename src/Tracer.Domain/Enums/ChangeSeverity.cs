namespace Tracer.Domain.Enums;

/// <summary>
/// Classifies the business impact of a detected change in a company profile field.
/// Drives notification routing and re-validation priority.
/// </summary>
/// <remarks>
/// Values are ordered from least to most severe (higher number = higher severity).
/// This ensures <c>default(ChangeSeverity)</c> produces <see cref="Cosmetic"/> —
/// the safest fallback — rather than triggering immediate notifications for
/// uninitialised or incorrectly deserialised values (CWE-1188 fail-safe default).
/// </remarks>
public enum ChangeSeverity
{
    /// <summary>
    /// Confidence score update, minor formatting normalisation.
    /// Recorded in history only; no notification.
    /// </summary>
    Cosmetic = 0,

    /// <summary>
    /// Phone, email, website, or operating address changed.
    /// Recorded in history; notification deferred to next query.
    /// </summary>
    Minor = 1,

    /// <summary>
    /// Registered address changed, director/officer change, legal name change.
    /// Notification sent within 24 hours.
    /// </summary>
    Major = 2,

    /// <summary>
    /// Company dissolved, in liquidation, or insolvency declared.
    /// Triggers immediate Service Bus notification on topic <c>tracer-changes</c>.
    /// </summary>
    Critical = 3,
}
