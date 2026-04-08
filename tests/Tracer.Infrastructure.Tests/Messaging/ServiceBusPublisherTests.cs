using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Tracer.Contracts.Enums;
using Tracer.Contracts.Messages;
using Tracer.Contracts.Models;
using Tracer.Infrastructure.Messaging;

namespace Tracer.Infrastructure.Tests.Messaging;

/// <summary>
/// Tests for <see cref="ServiceBusPublisher"/> — verifies that each publish method
/// routes to the correct sender and sets the expected Service Bus message metadata.
/// </summary>
public sealed class ServiceBusPublisherTests : IAsyncDisposable
{
    // CA2213 suppressed: _publisher.DisposeAsync() disposes all three senders internally.
    // _client and senders are NSubstitute mocks — their DisposeAsync is called via publisher disposal.
    #pragma warning disable CA2213
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _requestSender;
    private readonly ServiceBusSender _responseSender;
    private readonly ServiceBusSender _changesSender;
    private readonly ServiceBusPublisher _publisher;
    #pragma warning restore CA2213

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public ServiceBusPublisherTests()
    {
        _client = Substitute.For<ServiceBusClient>();
        _requestSender = Substitute.For<ServiceBusSender>();
        _responseSender = Substitute.For<ServiceBusSender>();
        _changesSender = Substitute.For<ServiceBusSender>();

        _client.CreateSender("tracer-request").Returns(_requestSender);
        _client.CreateSender("tracer-response").Returns(_responseSender);
        _client.CreateSender("tracer-changes").Returns(_changesSender);

        var options = Options.Create(new ServiceBusOptions
        {
            RequestQueue = "tracer-request",
            ResponseQueue = "tracer-response",
            ChangesTopic = "tracer-changes",
        });

        // NullLogger: ServiceBusPublisher is internal — Castle.DynamicProxy cannot proxy
        // ILogger<ServiceBusPublisher> from strong-named assemblies for internal types.
        _publisher = new ServiceBusPublisher(_client, options, NullLogger<ServiceBusPublisher>.Instance);
    }

    public ValueTask DisposeAsync() => _publisher.DisposeAsync();

    // ── EnqueueTraceRequestAsync ───────────────────────────────────────────────

    [Fact]
    public async Task EnqueueTraceRequestAsync_NullMessage_ThrowsArgumentNullException()
    {
        var act = async () => await _publisher.EnqueueTraceRequestAsync(null!, CancellationToken.None)
            .ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("message").ConfigureAwait(true);
    }

    [Fact]
    public async Task EnqueueTraceRequestAsync_ValidMessage_SendsToRequestSender()
    {
        var message = BuildTraceRequestMessage("corr-req-001");

        await _publisher.EnqueueTraceRequestAsync(message, CancellationToken.None).ConfigureAwait(true);

        await _requestSender.Received(1).SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);

        // Verify no other sender was called
        await _responseSender.DidNotReceive().SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);
        await _changesSender.DidNotReceive().SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);
    }

    [Fact]
    public async Task EnqueueTraceRequestAsync_ValidMessage_SetsMessageIdAsCorrelationId()
    {
        const string correlationId = "corr-req-dup-detect";
        var message = BuildTraceRequestMessage(correlationId);
        ServiceBusMessage? captured = null;
        await _requestSender.SendMessageAsync(
            Arg.Do<ServiceBusMessage>(m => captured = m),
            Arg.Any<CancellationToken>()).ConfigureAwait(true);

        await _publisher.EnqueueTraceRequestAsync(message, CancellationToken.None).ConfigureAwait(true);

        captured.Should().NotBeNull();
        captured!.MessageId.Should().Be(correlationId,
            "MessageId = CorrelationId enables Service Bus duplicate detection");
        captured.CorrelationId.Should().Be(correlationId);
        captured.Subject.Should().Be("TraceRequest");
        captured.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task EnqueueTraceRequestAsync_ValidMessage_BodyContainsSerializedRequest()
    {
        const string correlationId = "corr-body-check";
        var message = BuildTraceRequestMessage(correlationId);
        ServiceBusMessage? captured = null;
        await _requestSender.SendMessageAsync(
            Arg.Do<ServiceBusMessage>(m => captured = m),
            Arg.Any<CancellationToken>()).ConfigureAwait(true);

        await _publisher.EnqueueTraceRequestAsync(message, CancellationToken.None).ConfigureAwait(true);

        captured.Should().NotBeNull();
        var deserialized = captured!.Body.ToObjectFromJson<TraceRequestMessage>(JsonOptions);
        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.Should().Be(correlationId);
        deserialized.CompanyName.Should().Be("ACME s.r.o.");
        deserialized.Country.Should().Be("CZ");
    }

    // ── SendTraceResponseAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task SendTraceResponseAsync_NullMessage_ThrowsArgumentNullException()
    {
        var act = async () => await _publisher.SendTraceResponseAsync(null!, CancellationToken.None)
            .ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("message").ConfigureAwait(true);
    }

    [Fact]
    public async Task SendTraceResponseAsync_ValidMessage_SendsToResponseSender()
    {
        var message = BuildTraceResponseMessage("corr-resp-001");

        await _publisher.SendTraceResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        await _responseSender.Received(1).SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);

        await _requestSender.DidNotReceive().SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);
        await _changesSender.DidNotReceive().SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);
    }

    [Fact]
    public async Task SendTraceResponseAsync_ValidMessage_SetsCorrelationIdAndSubject()
    {
        const string correlationId = "corr-resp-meta";
        var message = BuildTraceResponseMessage(correlationId);
        ServiceBusMessage? captured = null;
        await _responseSender.SendMessageAsync(
            Arg.Do<ServiceBusMessage>(m => captured = m),
            Arg.Any<CancellationToken>()).ConfigureAwait(true);

        await _publisher.SendTraceResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        captured.Should().NotBeNull();
        captured!.CorrelationId.Should().Be(correlationId);
        captured.Subject.Should().Be("TraceResponse");
        captured.ContentType.Should().Be("application/json");
    }

    // ── PublishChangeEventAsync ───────────────────────────────────────────────

    [Fact]
    public async Task PublishChangeEventAsync_NullMessage_ThrowsArgumentNullException()
    {
        var act = async () => await _publisher.PublishChangeEventAsync(null!, CancellationToken.None)
            .ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("message").ConfigureAwait(true);
    }

    [Fact]
    public async Task PublishChangeEventAsync_ValidMessage_SendsToChangesSender()
    {
        var message = BuildChangeEventMessage(ChangeSeverity.Critical, FieldName.EntityStatus);

        await _publisher.PublishChangeEventAsync(message, CancellationToken.None).ConfigureAwait(true);

        await _changesSender.Received(1).SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);

        await _requestSender.DidNotReceive().SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);
        await _responseSender.DidNotReceive().SendMessageAsync(
            Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ConfigureAwait(true);
    }

    [Fact]
    public async Task PublishChangeEventAsync_CriticalChange_SetsApplicationProperties()
    {
        var profileId = Guid.NewGuid();
        var message = BuildChangeEventMessage(ChangeSeverity.Critical, FieldName.EntityStatus, profileId);
        ServiceBusMessage? captured = null;
        await _changesSender.SendMessageAsync(
            Arg.Do<ServiceBusMessage>(m => captured = m),
            Arg.Any<CancellationToken>()).ConfigureAwait(true);

        await _publisher.PublishChangeEventAsync(message, CancellationToken.None).ConfigureAwait(true);

        captured.Should().NotBeNull();
        captured!.Subject.Should().Be("Critical");
        captured.ApplicationProperties.Should().ContainKey("CompanyProfileId")
            .WhoseValue.Should().Be(profileId.ToString());
        captured.ApplicationProperties.Should().ContainKey("Field")
            .WhoseValue.Should().Be("EntityStatus");
        captured.ApplicationProperties.Should().ContainKey("Severity")
            .WhoseValue.Should().Be("Critical");
        captured.ContentType.Should().Be("application/json");
    }

    [Theory]
    [InlineData(ChangeSeverity.Major, "Major")]
    [InlineData(ChangeSeverity.Minor, "Minor")]
    [InlineData(ChangeSeverity.Cosmetic, "Cosmetic")]
    public async Task PublishChangeEventAsync_VariousSeverities_SubjectMatchesSeverityName(
        ChangeSeverity severity, string expectedSubject)
    {
        var message = BuildChangeEventMessage(severity, FieldName.Phone);
        ServiceBusMessage? captured = null;
        await _changesSender.SendMessageAsync(
            Arg.Do<ServiceBusMessage>(m => captured = m),
            Arg.Any<CancellationToken>()).ConfigureAwait(true);

        await _publisher.PublishChangeEventAsync(message, CancellationToken.None).ConfigureAwait(true);

        captured!.Subject.Should().Be(expectedSubject);
        captured.ApplicationProperties["Severity"].Should().Be(expectedSubject);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static TraceRequestMessage BuildTraceRequestMessage(string correlationId) =>
        new()
        {
            CorrelationId = correlationId,
            CompanyName = "ACME s.r.o.",
            Country = "CZ",
            RegistrationId = "00177041",
            Depth = TraceDepth.Standard,
            Source = "fieldforce-crm",
        };

    private static TraceResponseMessage BuildTraceResponseMessage(string correlationId) =>
        new()
        {
            TraceId = Guid.NewGuid(),
            CorrelationId = correlationId,
            Status = TraceStatus.Completed,
            OverallConfidence = 0.9,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            DurationMs = 800,
        };

    private static ChangeEventMessage BuildChangeEventMessage(
        ChangeSeverity severity, FieldName field, Guid? profileId = null) =>
        new()
        {
            CompanyProfileId = profileId ?? Guid.NewGuid(),
            NormalizedKey = "CZ:00177041",
            ChangeEvent = new ChangeEventContract
            {
                Id = Guid.NewGuid(),
                Field = field,
                ChangeType = ChangeType.Updated,
                Severity = severity,
                PreviousValueJson = "\"Active\"",
                NewValueJson = "\"Dissolved\"",
                DetectedBy = "ares",
                DetectedAt = DateTimeOffset.UtcNow,
            },
        };
}
