using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Application.Commands.SubmitTrace;
using Tracer.Application.Messaging;

namespace Tracer.Infrastructure.Messaging;

/// <summary>
/// Background service that listens on the <c>tracer-request</c> queue,
/// processes enrichment requests via MediatR, and publishes results
/// to the <c>tracer-response</c> queue.
/// </summary>
public sealed partial class ServiceBusConsumer : BackgroundService, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly IServiceProvider _serviceProvider;
    private readonly ServiceBusOptions _options;
    private readonly ILogger<ServiceBusConsumer> _logger;

    private ServiceBusProcessor? _processor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ServiceBusConsumer(
        ServiceBusClient client,
        IOptions<ServiceBusOptions> options,
        IServiceProvider serviceProvider,
        ILogger<ServiceBusConsumer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _client = client;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor(_options.RequestQueue, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 5,
            AutoCompleteMessages = false,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;

        LogConsumerStarting(_options.RequestQueue);

        await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        LogConsumerStopping();
        await _processor.StopProcessingAsync(CancellationToken.None).ConfigureAwait(false);
    }

    #pragma warning disable CA1031 // Intentional: message handler must catch all exceptions for dead-lettering
    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var correlationId = args.Message.CorrelationId ?? args.Message.MessageId;

        try
        {
            LogMessageReceived(correlationId);

            var requestMessage = args.Message.Body.ToObjectFromJson<TraceRequestMessage>(JsonOptions);
            if (requestMessage?.Input is null)
            {
                LogInvalidMessage(correlationId);
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "InvalidMessage",
                    deadLetterErrorDescription: "Could not deserialize TraceRequestMessage or Input is null.")
                    .ConfigureAwait(false);
                return;
            }

            #pragma warning disable CA2007 // ConfigureAwait on AsyncServiceScope loses ServiceProvider access
            await using var scope = _serviceProvider.CreateAsyncScope();
            #pragma warning restore CA2007
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var publisher = scope.ServiceProvider.GetRequiredService<IServiceBusPublisher>();

            var command = new SubmitTraceCommand
            {
                Input = requestMessage.Input,
                Source = requestMessage.Source,
            };

            var result = await mediator.Send(command, args.CancellationToken).ConfigureAwait(false);

            await publisher.SendTraceResponseAsync(new TraceResponseMessage
            {
                CorrelationId = correlationId,
                Result = result,
            }, args.CancellationToken).ConfigureAwait(false);

            await args.CompleteMessageAsync(args.Message).ConfigureAwait(false);

            var status = result.Status.ToString();
            LogMessageProcessed(correlationId, status);
        }
        catch (Exception ex)
        {
            LogMessageFailed(ex, correlationId);

            try
            {
                var errorDesc = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "ProcessingFailed",
                    deadLetterErrorDescription: errorDesc)
                    .ConfigureAwait(false);
            }
            catch (Exception dlEx)
            {
                LogDeadLetterFailed(dlEx, correlationId);
            }
        }
    }
    #pragma warning restore CA1031

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        var errorSource = args.ErrorSource.ToString();
        LogProcessorError(args.Exception, errorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Service Bus consumer starting on queue {QueueName}")]
    private partial void LogConsumerStarting(string queueName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Service Bus consumer stopping")]
    private partial void LogConsumerStopping();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Service Bus: Received message {CorrelationId}")]
    private partial void LogMessageReceived(string correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Service Bus: Invalid message {CorrelationId} — dead-lettered")]
    private partial void LogInvalidMessage(string correlationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Service Bus: Processed message {CorrelationId} → {Status}")]
    private partial void LogMessageProcessed(string correlationId, string status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Service Bus: Message {CorrelationId} failed")]
    private partial void LogMessageFailed(Exception ex, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Service Bus: Dead-letter failed for {CorrelationId}")]
    private partial void LogDeadLetterFailed(Exception ex, string correlationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Service Bus: Processor error from {ErrorSource}")]
    private partial void LogProcessorError(Exception ex, string errorSource);
}
