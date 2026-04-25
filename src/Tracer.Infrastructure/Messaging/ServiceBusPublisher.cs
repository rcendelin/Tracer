using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Application.Messaging;
using Tracer.Contracts.Messages;

namespace Tracer.Infrastructure.Messaging;

/// <summary>
/// Azure Service Bus publisher for trace responses and change events.
/// Registered as Singleton — ServiceBusClient manages connections internally.
/// </summary>
internal sealed partial class ServiceBusPublisher : IServiceBusPublisher, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _requestSender;
    private readonly ServiceBusSender _responseSender;
    private readonly ServiceBusSender _changesSender;
    private readonly ILogger<ServiceBusPublisher> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ServiceBusPublisher(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        ILogger<ServiceBusPublisher> logger)
    {
        _client = client;
        _logger = logger;

        var config = options.Value;
        _requestSender = client.CreateSender(config.RequestQueue);
        _responseSender = client.CreateSender(config.ResponseQueue);
        _changesSender = client.CreateSender(config.ChangesTopic);
    }

    public async Task SendTraceResponseAsync(TraceResponseMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sbMessage = new ServiceBusMessage(
            BinaryData.FromObjectAsJson(message, JsonOptions))
        {
            ContentType = "application/json",
            CorrelationId = message.CorrelationId,
            Subject = "TraceResponse",
        };

        await _responseSender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);

        LogResponseSent(message.CorrelationId);
    }

    public async Task PublishChangeEventAsync(ChangeEventMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var severityStr = message.ChangeEvent.Severity.ToString();
        var fieldStr = message.ChangeEvent.Field.ToString();

        var sbMessage = new ServiceBusMessage(
            BinaryData.FromObjectAsJson(message, JsonOptions))
        {
            ContentType = "application/json",
            Subject = severityStr,
            ApplicationProperties =
            {
                ["CompanyProfileId"] = message.CompanyProfileId.ToString(),
                ["Field"] = fieldStr,
                ["Severity"] = severityStr,
            },
        };

        await _changesSender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);

        LogChangePublished(message.NormalizedKey, fieldStr, severityStr);
    }

    public async Task EnqueueTraceRequestAsync(TraceRequestMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sbMessage = new ServiceBusMessage(
            BinaryData.FromObjectAsJson(message, JsonOptions))
        {
            ContentType = "application/json",
            // MessageId = CorrelationId enables Service Bus duplicate detection:
            // re-sending the same batch won't create duplicate TraceRequests if the
            // queue has RequiresDuplicateDetection=true (configured in Bicep).
            MessageId = message.CorrelationId,
            CorrelationId = message.CorrelationId,
            Subject = "TraceRequest",
        };

        await _requestSender.SendMessageAsync(sbMessage, cancellationToken).ConfigureAwait(false);

        LogRequestEnqueued(message.CorrelationId);
    }

    public async ValueTask DisposeAsync()
    {
        await _requestSender.DisposeAsync().ConfigureAwait(false);
        await _responseSender.DisposeAsync().ConfigureAwait(false);
        await _changesSender.DisposeAsync().ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Service Bus: Enqueued trace request with correlation {CorrelationId}")]
    private partial void LogRequestEnqueued(string correlationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Service Bus: Sent trace response for correlation {CorrelationId}")]
    private partial void LogResponseSent(string correlationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Service Bus: Published change event for {NormalizedKey} field {Field} severity {Severity}")]
    private partial void LogChangePublished(string normalizedKey, string field, string severity);
}

/// <summary>
/// Configuration for Service Bus queues and topics.
/// </summary>
public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    public string ResponseQueue { get; set; } = "tracer-response";
    public string ChangesTopic { get; set; } = "tracer-changes";
    public string RequestQueue { get; set; } = "tracer-request";
}
