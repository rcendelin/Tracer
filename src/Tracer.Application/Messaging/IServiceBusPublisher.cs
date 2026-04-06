namespace Tracer.Application.Messaging;

/// <summary>
/// Publishes messages to Azure Service Bus queues and topics.
/// </summary>
public interface IServiceBusPublisher
{
    /// <summary>
    /// Sends a trace response to the <c>tracer-response</c> queue.
    /// </summary>
    Task SendTraceResponseAsync(TraceResponseMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a change event to the <c>tracer-changes</c> topic.
    /// </summary>
    Task PublishChangeEventAsync(ChangeEventMessage message, CancellationToken cancellationToken = default);
}
