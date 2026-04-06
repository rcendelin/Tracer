namespace Tracer.Contracts.Enums;

/// <summary>
/// Classifies the business impact of a detected change in a company profile field.
/// Carried in <see cref="Messages.ChangeEventMessage"/> to allow consumers to apply
/// different handling logic (e.g. FieldForce CRM alert thresholds).
/// </summary>
/// <remarks>
/// Service Bus subscription filter on topic <c>tracer-changes</c> routes only
/// <see cref="Critical"/> and <see cref="Major"/> changes to the default subscription.
/// Subscribe with a custom filter to also receive <see cref="Minor"/> changes.
/// </remarks>
public enum ChangeSeverity
{
    /// <summary>
    /// Confidence score update or minor formatting normalisation.
    /// Not published to the Service Bus topic — recorded in Tracer history only.
    /// </summary>
    Cosmetic = 0,

    /// <summary>
    /// Phone, email, website, or operating address changed.
    /// Published to <c>tracer-changes</c> topic; not routed to default subscription.
    /// Subscribe with filter <c>Severity = 'Minor'</c> to receive these.
    /// </summary>
    Minor = 1,

    /// <summary>
    /// Registered address changed, director/officer change, or legal name change.
    /// Published to <c>tracer-changes</c> topic; routed to default subscription.
    /// </summary>
    Major = 2,

    /// <summary>
    /// Company dissolved, in liquidation, or insolvency declared.
    /// Triggers immediate notification on topic <c>tracer-changes</c>.
    /// FieldForce should flag the associated account immediately for review.
    /// </summary>
    Critical = 3,
}
