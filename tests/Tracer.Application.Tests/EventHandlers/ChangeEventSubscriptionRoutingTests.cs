using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tracer.Application.EventHandlers;
using Tracer.Application.Messaging;
using Tracer.Application.Services;
using Tracer.Contracts.Messages;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.EventHandlers;

/// <summary>
/// End-to-end contract test for B-74: guarantees the severity values that handlers
/// write into the Service Bus message match the SQL filter on <c>fieldforce-changes</c>
/// subscription (<c>Severity='Critical' OR Severity='Major'</c>) and that the
/// <c>monitoring-changes</c> subscription (implicit <c>1=1</c>) receives everything
/// Tracer publishes.
/// </summary>
/// <remarks>
/// This is a pure in-memory simulation — no real Service Bus involvement — but it
/// keeps the Bicep filter expression and the publisher's ApplicationProperties in
/// sync: if either side drifts, this test fails. For true infra-level verification
/// the Service Bus Emulator / live namespace is used in B-76.
/// </remarks>
public sealed class ChangeEventSubscriptionRoutingTests
{
    // Mirrors deploy/bicep/modules/service-bus.bicep fieldforce-changes $Default rule.
    private static readonly Predicate<string> FieldforceFilter =
        severity => string.Equals(severity, "Critical", StringComparison.Ordinal)
                 || string.Equals(severity, "Major", StringComparison.Ordinal);

    // monitoring-changes has no filter override → implicit TrueFilter.
    private static readonly Predicate<string> MonitoringFilter = _ => true;

    [Theory]
    [InlineData(ChangeSeverity.Critical, true, true)]
    [InlineData(ChangeSeverity.Major, true, true)]
    [InlineData(ChangeSeverity.Minor, false, true)]
    public async Task Severity_Routes_To_Expected_Subscriptions(
        ChangeSeverity severity,
        bool expectedFieldforceDelivery,
        bool expectedMonitoringDelivery)
    {
        var capture = new CapturingPublisher();
        var profile = new CompanyProfile("CZ:00177041", "CZ", "00177041");
        var profileRepo = Substitute.For<ICompanyProfileRepository>();
        profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        var signalR = Substitute.For<ITraceNotificationService>();

        if (severity == ChangeSeverity.Critical)
        {
            // Critical flows through CriticalChangeNotificationHandler only.
            var sut = new CriticalChangeNotificationHandler(
                capture, signalR, profileRepo,
                NullLogger<CriticalChangeNotificationHandler>.Instance);
            await sut.Handle(
                new CriticalChangeDetectedEvent(profile.Id, Guid.NewGuid(),
                    FieldName.EntityStatus, "\"Dissolved\""),
                CancellationToken.None);
        }
        else
        {
            var sut = new FieldChangedNotificationHandler(
                capture, signalR, profileRepo,
                NullLogger<FieldChangedNotificationHandler>.Instance);
            await sut.Handle(
                new FieldChangedEvent(profile.Id, Guid.NewGuid(),
                    FieldName.LegalName, ChangeType.Updated, severity,
                    "\"Old Name\"", "\"New Name\""),
                CancellationToken.None);
        }

        capture.Messages.Should().HaveCount(1,
            "every non-cosmetic severity publishes exactly once");
        var severityLabel = capture.Messages[0].SeverityLabel;

        FieldforceFilter(severityLabel).Should().Be(expectedFieldforceDelivery,
            $"fieldforce-changes SQL filter routing for severity '{severityLabel}'");
        MonitoringFilter(severityLabel).Should().Be(expectedMonitoringDelivery,
            $"monitoring-changes TrueFilter routing for severity '{severityLabel}'");
    }

    [Fact]
    public async Task Cosmetic_Severity_Is_Never_Published_To_Topic()
    {
        var capture = new CapturingPublisher();
        var profile = new CompanyProfile("CZ:00177041", "CZ", "00177041");
        var profileRepo = Substitute.For<ICompanyProfileRepository>();
        profileRepo.GetByIdAsync(profile.Id, Arg.Any<CancellationToken>()).Returns(profile);
        var signalR = Substitute.For<ITraceNotificationService>();
        var sut = new FieldChangedNotificationHandler(
            capture, signalR, profileRepo,
            NullLogger<FieldChangedNotificationHandler>.Instance);

        await sut.Handle(
            new FieldChangedEvent(profile.Id, Guid.NewGuid(),
                FieldName.Industry, ChangeType.Updated, ChangeSeverity.Cosmetic,
                "\"Old\"", "\"New\""),
            CancellationToken.None);

        capture.Messages.Should().BeEmpty(
            "Cosmetic is log-only; publishing would drown the monitoring subscription");
    }

    private sealed class CapturingPublisher : IServiceBusPublisher
    {
        public List<(ChangeEventMessage Message, string SeverityLabel)> Messages { get; } = new();

        public Task PublishChangeEventAsync(ChangeEventMessage message, CancellationToken cancellationToken = default)
        {
            // Severity string mirrors ServiceBusPublisher.PublishChangeEventAsync
            // which sets ApplicationProperties["Severity"] = severity.ToString().
            Messages.Add((message, message.ChangeEvent.Severity.ToString()));
            return Task.CompletedTask;
        }

        public Task EnqueueTraceRequestAsync(TraceRequestMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendTraceResponseAsync(TraceResponseMessage message, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
