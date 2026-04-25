// ─────────────────────────────────────────────────────────────────────────────
// FieldForce — Tracer Integration: Consumer Skeleton
// ─────────────────────────────────────────────────────────────────────────────
//
// Package reference:  <PackageReference Include="Tracer.Contracts" Version="1.*" />
// Service Bus SDK:    <PackageReference Include="Azure.Messaging.ServiceBus" Version="7.*" />
//
// This file demonstrates the recommended pattern for FieldForce (or any other
// upstream/downstream service) to integrate with Tracer via Azure Service Bus.
//
// Queues / topics:
//   tracer-request     — FieldForce sends TraceRequestMessage here
//   tracer-response    — FieldForce receives TraceResponseMessage from here
//   tracer-changes     — FieldForce receives ChangeEventMessage from here
//                        (default subscription: Critical + Major severity only)

using Azure.Messaging.ServiceBus;
using System.Text.Json;
using Tracer.Contracts.Enums;
using Tracer.Contracts.Messages;
using Tracer.Contracts.Models;

namespace FieldForce.Integration.Tracer;

// ─────────────────────────────────────────────────────────────────────────────
// 1. Sending an enrichment request
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Sends enrichment requests to Tracer and stores the correlation for matching responses.
/// </summary>
public sealed class TracerEnrichmentClient : IAsyncDisposable
{
    private readonly ServiceBusSender _requestSender;
    private readonly ICorrelationStore _correlationStore;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public TracerEnrichmentClient(ServiceBusClient client, ICorrelationStore correlationStore)
    {
        _requestSender = client.CreateSender("tracer-request");
        _correlationStore = correlationStore;
    }

    /// <summary>
    /// Submits an enrichment request for a FieldForce account and returns the correlation ID
    /// that will be echoed back in the <see cref="TraceResponseMessage"/>.
    /// </summary>
    public async Task<string> RequestEnrichmentAsync(
        string fieldForceAccountId,
        string companyName,
        string? registrationId,
        string country,
        TraceDepth depth = TraceDepth.Standard,
        CancellationToken cancellationToken = default)
    {
        // Use a stable, idempotency-safe correlation ID so duplicate sends are safe
        var correlationId = $"ff-{fieldForceAccountId}-{DateTimeOffset.UtcNow:yyyyMMddHH}";

        var message = new TraceRequestMessage
        {
            CorrelationId = correlationId,
            CompanyName = companyName,
            RegistrationId = registrationId,
            Country = country,
            Depth = depth,
            Source = "fieldforce-crm",
        };

        var sbMessage = new ServiceBusMessage(BinaryData.FromObjectAsJson(message, JsonOptions))
        {
            ContentType = "application/json",
            CorrelationId = correlationId,
            MessageId = correlationId,  // Enables Service Bus duplicate detection
            Subject = "TraceRequest",
        };

        await _requestSender.SendMessageAsync(sbMessage, cancellationToken);

        // Store the mapping so we can look up the FieldForce account when the response arrives
        await _correlationStore.StoreAsync(correlationId, fieldForceAccountId, cancellationToken);

        return correlationId;
    }

    public async ValueTask DisposeAsync() => await _requestSender.DisposeAsync();
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. Receiving enrichment responses
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Background service that receives completed enrichment results from Tracer
/// and updates FieldForce accounts with the enriched data.
/// </summary>
public sealed class TracerResponseConsumer : BackgroundService, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ICorrelationStore _correlationStore;
    private readonly IFieldForceAccountService _accountService;
    private ServiceBusProcessor? _processor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public TracerResponseConsumer(
        ServiceBusClient client,
        ICorrelationStore correlationStore,
        IFieldForceAccountService accountService)
    {
        _client = client;
        _correlationStore = correlationStore;
        _accountService = accountService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor("tracer-response", new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = 10,
            AutoCompleteMessages = false,
        });

        _processor.ProcessMessageAsync += ProcessResponseAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown
        }
    }

    private async Task ProcessResponseAsync(ProcessMessageEventArgs args)
    {
        var response = args.Message.Body.ToObjectFromJson<TraceResponseMessage>(JsonOptions);
        if (response is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "InvalidMessage", "Cannot deserialize TraceResponseMessage");
            return;
        }

        // Idempotency: ignore duplicates using TraceId
        if (await _accountService.IsAlreadyProcessedAsync(response.TraceId))
        {
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        // Look up the FieldForce account via the correlation ID
        var accountId = await _correlationStore.GetAsync(response.CorrelationId, args.CancellationToken);
        if (accountId is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "UnknownCorrelation", $"No account for correlation {response.CorrelationId}");
            return;
        }

        // Only update CRM with high-confidence data
        if (response.Status is TraceStatus.Completed or TraceStatus.PartiallyCompleted
            && response.Company is not null)
        {
            await UpdateAccountFromEnrichedDataAsync(accountId, response.Company, args.CancellationToken);
        }

        await _accountService.MarkProcessedAsync(response.TraceId, args.CancellationToken);
        await args.CompleteMessageAsync(args.Message);
    }

    private async Task UpdateAccountFromEnrichedDataAsync(
        string accountId,
        EnrichedCompanyContract company,
        CancellationToken cancellationToken)
    {
        // Apply enriched fields to the FieldForce CRM account.
        // Only write fields above your confidence threshold.
        const double MinConfidence = 0.8;

        if (company.LegalName?.Confidence >= MinConfidence)
            await _accountService.SetLegalNameAsync(accountId, company.LegalName.Value, cancellationToken);

        if (company.Phone?.Confidence >= MinConfidence)
            await _accountService.SetPhoneAsync(accountId, company.Phone.Value, cancellationToken);

        if (company.Email?.Confidence >= MinConfidence)
            await _accountService.SetEmailAsync(accountId, company.Email.Value, cancellationToken);

        if (company.Website?.Confidence >= MinConfidence)
            await _accountService.SetWebsiteAsync(accountId, company.Website.Value, cancellationToken);

        if (company.RegisteredAddress?.Confidence >= MinConfidence)
        {
            var addr = company.RegisteredAddress.Value;
            await _accountService.SetAddressAsync(accountId, addr.FormattedAddress ?? addr.Street, cancellationToken);
        }

        if (company.EntityStatus?.Confidence >= MinConfidence)
            await _accountService.SetEntityStatusAsync(accountId, company.EntityStatus.Value, cancellationToken);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        // Log the error — in production use ILogger<TracerResponseConsumer>
        Console.Error.WriteLine($"[TracerResponseConsumer] Error: {args.Exception.Message}");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
            await _processor.DisposeAsync();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. Receiving change event notifications
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Background service that receives company change notifications from Tracer
/// and triggers appropriate FieldForce workflows (alerts, re-scoring, etc.).
/// </summary>
/// <remarks>
/// The default <c>tracer-changes</c> subscription routes only Critical and Major changes.
/// To also receive Minor changes, create a separate subscription with filter:
///   <c>Severity = 'Minor'</c>
/// </remarks>
public sealed class TracerChangeConsumer : BackgroundService, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly IFieldForceNotificationService _notificationService;
    private ServiceBusProcessor? _processor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public TracerChangeConsumer(ServiceBusClient client, IFieldForceNotificationService notificationService)
    {
        _client = client;
        _notificationService = notificationService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Subscribe to the default subscription (Critical + Major only)
        _processor = _client.CreateProcessor(
            topicName: "tracer-changes",
            subscriptionName: "fieldforce-crm",   // create this subscription in your Bicep/ARM
            new ServiceBusProcessorOptions { MaxConcurrentCalls = 5, AutoCompleteMessages = false });

        _processor.ProcessMessageAsync += ProcessChangeAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown
        }
    }

    private async Task ProcessChangeAsync(ProcessMessageEventArgs args)
    {
        var changeMessage = args.Message.Body.ToObjectFromJson<ChangeEventMessage>(JsonOptions);
        if (changeMessage is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "InvalidMessage", "Cannot deserialize ChangeEventMessage");
            return;
        }

        var change = changeMessage.ChangeEvent;

        // Idempotency — use change.Id as the deduplication key
        if (await _notificationService.IsAlreadyHandledAsync(change.Id))
        {
            await args.CompleteMessageAsync(args.Message);
            return;
        }

        switch (change.Severity)
        {
            case ChangeSeverity.Critical:
                // Company dissolved / in liquidation — flag the account immediately
                await _notificationService.RaiseCriticalAlertAsync(
                    companyProfileId: changeMessage.CompanyProfileId,
                    normalizedKey: changeMessage.NormalizedKey,
                    field: change.Field.ToString(),
                    newValue: change.NewValueJson,
                    cancellationToken: args.CancellationToken);
                break;

            case ChangeSeverity.Major:
                // Address change, name change, officer change — notify account manager
                await _notificationService.RaiseMajorAlertAsync(
                    companyProfileId: changeMessage.CompanyProfileId,
                    normalizedKey: changeMessage.NormalizedKey,
                    field: change.Field.ToString(),
                    changeType: change.ChangeType.ToString(),
                    newValue: change.NewValueJson,
                    cancellationToken: args.CancellationToken);
                break;

            default:
                // Minor / Cosmetic: log and move on (not normally routed to this subscription)
                break;
        }

        await _notificationService.MarkHandledAsync(change.Id, args.CancellationToken);
        await args.CompleteMessageAsync(args.Message);
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        Console.Error.WriteLine($"[TracerChangeConsumer] Error: {args.Exception.Message}");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
            await _processor.DisposeAsync();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Supporting interfaces (implement these in FieldForce)
// ─────────────────────────────────────────────────────────────────────────────

public interface ICorrelationStore
{
    Task StoreAsync(string correlationId, string accountId, CancellationToken cancellationToken);
    Task<string?> GetAsync(string correlationId, CancellationToken cancellationToken);
}

public interface IFieldForceAccountService
{
    Task<bool> IsAlreadyProcessedAsync(Guid traceId);
    Task MarkProcessedAsync(Guid traceId, CancellationToken cancellationToken);
    Task SetLegalNameAsync(string accountId, string name, CancellationToken cancellationToken);
    Task SetPhoneAsync(string accountId, string phone, CancellationToken cancellationToken);
    Task SetEmailAsync(string accountId, string email, CancellationToken cancellationToken);
    Task SetWebsiteAsync(string accountId, string website, CancellationToken cancellationToken);
    Task SetAddressAsync(string accountId, string address, CancellationToken cancellationToken);
    Task SetEntityStatusAsync(string accountId, string status, CancellationToken cancellationToken);
}

public interface IFieldForceNotificationService
{
    Task<bool> IsAlreadyHandledAsync(Guid changeEventId);
    Task MarkHandledAsync(Guid changeEventId, CancellationToken cancellationToken);
    Task RaiseCriticalAlertAsync(Guid companyProfileId, string normalizedKey, string field, string? newValue, CancellationToken cancellationToken);
    Task RaiseMajorAlertAsync(Guid companyProfileId, string normalizedKey, string field, string changeType, string? newValue, CancellationToken cancellationToken);
}
