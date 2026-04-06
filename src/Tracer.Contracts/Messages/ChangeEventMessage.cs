using Tracer.Contracts.Enums;
using Tracer.Contracts.Models;

namespace Tracer.Contracts.Messages;

/// <summary>
/// Message sent by Tracer when a significant change is detected in a monitored company profile.
/// </summary>
/// <remarks>
/// <para><strong>Transport:</strong> Azure Service Bus topic <c>tracer-changes</c>.</para>
/// <para><strong>Serialisation:</strong> JSON (camelCase), UTF-8, Content-Type: <c>application/json</c>.</para>
/// <para>
/// <strong>Subscription filter (default):</strong> The default subscription on <c>tracer-changes</c>
/// delivers only <see cref="ChangeSeverity.Critical"/> and <see cref="ChangeSeverity.Major"/> changes.
/// Filter: <c>Severity = 'Critical' OR Severity = 'Major'</c> (SQL filter on message property).
/// </para>
/// <para>
/// To also receive <see cref="ChangeSeverity.Minor"/> changes, create a separate subscription
/// with filter <c>Severity = 'Minor'</c> (or <c>1=1</c> for all severities).
/// </para>
/// <para><strong>Idempotency:</strong> Use <see cref="ChangeEvent"/>.<see cref="ChangeEventContract.Id"/>
/// as an idempotency key — the same change event may be delivered more than once under Service Bus
/// at-least-once delivery guarantees.</para>
/// <para>
/// <strong>Message properties</strong> set by Tracer for server-side filtering:
/// <list type="table">
///   <listheader><term>Property</term><description>Value</description></listheader>
///   <item><term><c>Severity</c></term><description>e.g. <c>"Critical"</c>, <c>"Major"</c></description></item>
///   <item><term><c>Field</c></term><description>e.g. <c>"EntityStatus"</c>, <c>"RegisteredAddress"</c></description></item>
///   <item><term><c>CompanyProfileId</c></term><description>UUID of the affected profile</description></item>
/// </list>
/// </para>
/// </remarks>
/// <example>
/// Critical change — company dissolved:
/// <code>
/// {
///   "companyProfileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
///   "normalizedKey": "CZ:00177041",
///   "changeEvent": {
///     "id": "a1b2c3d4-1234-5678-abcd-ef0123456789",
///     "field": 12,
///     "changeType": 1,
///     "severity": 3,
///     "previousValueJson": "\"Active\"",
///     "newValueJson": "\"Dissolved\"",
///     "detectedBy": "ares",
///     "detectedAt": "2026-04-06T08:15:00Z"
///   }
/// }
/// </code>
/// </example>
public sealed record ChangeEventMessage
{
    /// <summary>
    /// Unique identifier of the affected company profile in Tracer's CKB.
    /// Use this to correlate with the FieldForce account via your integration mapping.
    /// </summary>
    public required Guid CompanyProfileId { get; init; }

    /// <summary>
    /// Normalized company key in Tracer (format: <c>{CountryCode}:{RegistrationId}</c>, e.g. <c>"CZ:00177041"</c>).
    /// Stable identifier that survives company renames.
    /// </summary>
    public required string NormalizedKey { get; init; }

    /// <summary>Details of the detected change.</summary>
    public required ChangeEventContract ChangeEvent { get; init; }
}
