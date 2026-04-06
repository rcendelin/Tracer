using Tracer.Application.Messaging;
using Tracer.Contracts.Messages;

namespace Tracer.Infrastructure.Messaging;

/// <summary>
/// No-op publisher used when Service Bus is not configured.
/// Silently discards messages — appropriate for local development.
/// </summary>
internal sealed class NullServiceBusPublisher : IServiceBusPublisher
{
    public Task SendTraceResponseAsync(TraceResponseMessage message, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task PublishChangeEventAsync(ChangeEventMessage message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
