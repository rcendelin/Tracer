using Tracer.Contracts.Messages;

namespace Tracer.Application.Messaging;

/// <summary>
/// Publishes messages to Azure Service Bus queues and topics.
/// </summary>
/// <remarks>
/// Message types are defined in <see cref="Tracer.Contracts"/> — the shared NuGet package
/// that FieldForce references to consume Tracer's Service Bus interface.
/// </remarks>
public interface IServiceBusPublisher
{
    /// <summary>
    /// Sends a trace response to the <c>tracer-response</c> queue.
    /// </summary>
    /// <param name="message">The response message built from a completed trace result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendTraceResponseAsync(TraceResponseMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a change event to the <c>tracer-changes</c> topic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The publisher tags each message with <c>ApplicationProperties["Severity"]</c>;
    /// topic subscriptions filter on that property:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>fieldforce-changes</c> — SQL filter <c>Severity='Critical' OR Severity='Major'</c>.</description></item>
    ///   <item><description><c>monitoring-changes</c> — implicit <c>1=1</c> (receives everything published).</description></item>
    /// </list>
    /// <para>
    /// Tracer callers never publish <see cref="Tracer.Contracts.Enums.ChangeSeverity.Cosmetic"/>
    /// (log-only); <see cref="Tracer.Contracts.Enums.ChangeSeverity.Minor"/> goes to the topic so
    /// monitoring sees it, while the FieldForce CRM does not.
    /// </para>
    /// </remarks>
    /// <param name="message">The change event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishChangeEventAsync(ChangeEventMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enqueues a trace request to the <c>tracer-request</c> queue for async processing.
    /// Used by the batch endpoint to submit requests for background enrichment.
    /// Sets <c>MessageId = CorrelationId</c> to enable Service Bus duplicate detection.
    /// </summary>
    /// <param name="message">The enrichment request to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EnqueueTraceRequestAsync(TraceRequestMessage message, CancellationToken cancellationToken = default);
}
