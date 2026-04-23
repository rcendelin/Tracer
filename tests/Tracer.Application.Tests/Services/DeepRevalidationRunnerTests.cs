using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="DeepRevalidationRunner"/> — gatekeeping, synthetic
/// TraceRequest construction, audit persistence and cancellation handling.
/// </summary>
public sealed class DeepRevalidationRunnerTests
{
    private readonly IFieldTtlPolicy _ttlPolicy = Substitute.For<IFieldTtlPolicy>();
    private readonly IWaterfallOrchestrator _orchestrator = Substitute.For<IWaterfallOrchestrator>();
    private readonly ITraceRequestRepository _traceRepo = Substitute.For<ITraceRequestRepository>();
    private readonly IValidationRecordRepository _validationRepo = Substitute.For<IValidationRecordRepository>();
    private readonly IChangeEventRepository _changeEventRepo = Substitute.For<IChangeEventRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static readonly DateTimeOffset FixedNow = new(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);

    private DeepRevalidationRunner CreateSut(int threshold = 3)
    {
        var options = Options.Create(new DeepRevalidationOptions { Threshold = threshold });
        return new DeepRevalidationRunner(
            _ttlPolicy,
            _orchestrator,
            _traceRepo,
            _validationRepo,
            _changeEventRepo,
            _unitOfWork,
            options,
            NullLogger<DeepRevalidationRunner>.Instance)
        {
            Clock = () => FixedNow,
        };
    }

    private static CompanyProfile CreateProfile(
        string registrationId = "12345678",
        string country = "CZ",
        string? legalName = "Acme s.r.o.",
        string? phone = null,
        string? email = null,
        string? website = null,
        string? taxId = null)
    {
        var profile = new CompanyProfile($"{country}:{registrationId}", country, registrationId);
        if (legalName is not null)
            profile.UpdateField(FieldName.LegalName, StringField(legalName), "ares");
        if (phone is not null)
            profile.UpdateField(FieldName.Phone, StringField(phone), "ares");
        if (email is not null)
            profile.UpdateField(FieldName.Email, StringField(email), "ares");
        if (website is not null)
            profile.UpdateField(FieldName.Website, StringField(website), "ares");
        if (taxId is not null)
            profile.UpdateField(FieldName.TaxId, StringField(taxId), "ares");
        return profile;
    }

    private static TracedField<string> StringField(string value) => new()
    {
        Value = value,
        Confidence = Confidence.Create(0.9),
        Source = "ares",
        EnrichedAt = FixedNow,
    };

    /// <summary>
    /// Stubs <see cref="IWaterfallOrchestrator.ExecuteAsync"/> to return
    /// <paramref name="refreshed"/> and capture the submitted
    /// <see cref="TraceRequest"/> into <paramref name="captureSlot"/>.
    /// </summary>
    private void StubOrchestrator(CompanyProfile refreshed, Action<TraceRequest> captureSlot) =>
        _orchestrator
            .ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captureSlot(ci.Arg<TraceRequest>());
                return refreshed;
            });

    // ── Trigger / threshold ─────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NoExpiredFields_ReturnsDeferred()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(Array.Empty<FieldName>());
        var sut = CreateSut();

        var outcome = await sut.RunAsync(CreateProfile(), CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deferred);
        await _orchestrator.DidNotReceive()
            .ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ExpiredCountBelowThreshold_ReturnsDeferred()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone });
        var sut = CreateSut(threshold: 3);

        var outcome = await sut.RunAsync(CreateProfile(), CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deferred);
        await _orchestrator.DidNotReceive()
            .ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ConfigurableThresholdOne_TriggersOnSingleExpiredField()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus });
        var profile = CreateProfile();
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .Returns(profile);
        var sut = CreateSut(threshold: 1);

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deep);
        await _orchestrator.Received(1)
            .ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Feasibility gate ────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_MissingRegistrationId_DefersWithoutRunningWaterfall()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone, FieldName.Email });
        // RegistrationId deliberately null.
        var profile = new CompanyProfile("NAME:ACME", "CZ", registrationId: null);
        var sut = CreateSut();

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deferred);
        await _orchestrator.DidNotReceive()
            .ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Happy path ──────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ThresholdMet_BuildsSyntheticRequestWithStandardDepthAndRevalidationSource()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone, FieldName.Email });
        var profile = CreateProfile(
            registrationId: "00177041",
            country: "CZ",
            legalName: "Škoda Auto a.s.",
            phone: "+420123",
            email: "info@example.cz",
            taxId: "CZ00177041");

        TraceRequest? captured = null;
        StubOrchestrator(profile, r => captured = r);
        var sut = CreateSut();

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deep);
        captured.Should().NotBeNull();
        captured!.Source.Should().Be(DeepRevalidationRunner.TraceSourceTag);
        captured.Depth.Should().Be(TraceDepth.Standard);
        captured.RegistrationId.Should().Be("00177041");
        captured.Country.Should().Be("CZ");
        captured.CompanyName.Should().Be("Škoda Auto a.s.");
        captured.Phone.Should().Be("+420123");
        captured.Email.Should().Be("info@example.cz");
        captured.TaxId.Should().Be("CZ00177041");
        captured.CallbackUrl.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_HappyPath_PersistsValidationRecordAndMarksProfile()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone, FieldName.Email });

        var profile = CreateProfile();
        // Simulate change events delta: 2 before, 5 after → 3 changed.
        _changeEventRepo.CountByProfileAsync(profile.Id, Arg.Any<CancellationToken>())
            .Returns(2, 5);
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .Returns(profile);

        var sut = CreateSut();
        var beforeValidatedAt = profile.LastValidatedAt;

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deep);
        await _validationRepo.Received(1).AddAsync(
            Arg.Is<ValidationRecord>(r =>
                r.CompanyProfileId == profile.Id &&
                r.ValidationType == ValidationType.Deep &&
                r.FieldsChecked == 3 &&
                r.FieldsChanged == 3 &&
                r.ProviderId == DeepRevalidationRunner.ValidationProviderId &&
                r.DurationMs >= 0),
            Arg.Any<CancellationToken>());
        profile.LastValidatedAt.Should().NotBe(beforeValidatedAt);
        profile.LastValidatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_HappyPath_SavesTraceRequestInProgressAndThenCompletes()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone, FieldName.Email });
        var profile = CreateProfile();
        TraceRequest? captured = null;
        StubOrchestrator(profile, r => captured = r);
        var sut = CreateSut();

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deep);
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(TraceStatus.Completed);
        captured.CompanyProfileId.Should().Be(profile.Id);
        await _traceRepo.Received(1).AddAsync(captured, Arg.Any<CancellationToken>());
        // SaveChanges called twice: after AddAsync(traceRequest InProgress), and after completion + audit.
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_FieldsChangedDeltaNeverNegative()
    {
        // If the orchestrator somehow prunes change events the count could drop;
        // runner must clamp to 0 rather than persisting a negative count which would
        // otherwise throw inside ValidationRecord.
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone, FieldName.Email });
        var profile = CreateProfile();
        _changeEventRepo.CountByProfileAsync(profile.Id, Arg.Any<CancellationToken>())
            .Returns(5, 2);
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .Returns(profile);
        var sut = CreateSut();

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deep);
        await _validationRepo.Received(1).AddAsync(
            Arg.Is<ValidationRecord>(r => r.FieldsChanged == 0),
            Arg.Any<CancellationToken>());
    }

    // ── Error handling ──────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_OrchestratorThrows_ReturnsFailedAndMarksTraceRequestFailed()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone, FieldName.Email });
        var profile = CreateProfile();
        TraceRequest? captured = null;
        _traceRepo
            .When(x => x.AddAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<TraceRequest>());
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Failed);
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(TraceStatus.Failed);
        await _validationRepo.DidNotReceive()
            .AddAsync(Arg.Any<ValidationRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_OrchestratorThrows_DoesNotMarkProfileValidated()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone, FieldName.Email });
        var profile = CreateProfile();
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));
        var sut = CreateSut();

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Failed);
        profile.LastValidatedAt.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_OperationCanceled_PropagatesAndMarksTraceRequestFailed()
    {
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(new[] { FieldName.EntityStatus, FieldName.Phone, FieldName.Email });
        var profile = CreateProfile();
        TraceRequest? captured = null;
        _traceRepo
            .When(x => x.AddAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<TraceRequest>());
        _orchestrator.ExecuteAsync(Arg.Any<TraceRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());
        var sut = CreateSut();

        var act = async () => await sut.RunAsync(profile, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        captured.Should().NotBeNull();
        captured!.Status.Should().Be(TraceStatus.Failed);
    }

    // ── Argument guards ─────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NullProfile_Throws()
    {
        var sut = CreateSut();

        var act = async () => await sut.RunAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_CancelledTokenBeforeStart_Throws()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await sut.RunAsync(CreateProfile(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
