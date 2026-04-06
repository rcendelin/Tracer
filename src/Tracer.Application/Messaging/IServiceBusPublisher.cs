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
    /// Only <see cref="Tracer.Contracts.Enums.ChangeSeverity.Critical"/> and
    /// <see cref="Tracer.Contracts.Enums.ChangeSeverity.Major"/> events are routed
    /// to the default subscription; minor events require a custom subscription filter.
    /// </summary>
    /// <param name="message">The change event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishChangeEventAsync(ChangeEventMessage message, CancellationToken cancellationToken = default);
}
