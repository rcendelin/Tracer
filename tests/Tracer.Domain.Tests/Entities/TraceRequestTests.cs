using FluentAssertions;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Tests.Entities;

public sealed class TraceRequestTests
{
    private static TraceRequest CreateSut(
        string? companyName = "Acme s.r.o.",
        string? registrationId = "12345678",
        TraceDepth depth = TraceDepth.Standard,
        string source = "rest-api") =>
        new(
            companyName: companyName,
            phone: "+420 123 456 789",
            email: "info@acme.cz",
            website: "https://acme.cz",
            address: "Hlavní 1",
            city: "Praha",
            country: "CZ",
            registrationId: registrationId,
            taxId: "CZ12345678",
            industryHint: "IT",
            depth: depth,
            callbackUrl: null,
            source: source);

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidInput_CreatesRequestInPendingState()
    {
        var sut = CreateSut();

        sut.Status.Should().Be(TraceStatus.Pending);
        sut.CompanyName.Should().Be("Acme s.r.o.");
        sut.Source.Should().Be("rest-api");
        sut.Depth.Should().Be(TraceDepth.Standard);
        sut.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        sut.CompletedAt.Should().BeNull();
        sut.DurationMs.Should().BeNull();
        sut.CompanyProfileId.Should().BeNull();
        sut.OverallConfidence.Should().BeNull();
        sut.FailureReason.Should().BeNull();
        sut.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_TrimsWhitespace()
    {
        var sut = new TraceRequest(
            companyName: "  Acme  ",
            phone: "  +420  ",
            email: "  info@acme.cz  ",
            website: "  https://acme.cz  ",
            address: "  Hlavní 1  ",
            city: "  Praha  ",
            country: "  CZ  ",
            registrationId: "  123  ",
            taxId: "  CZ123  ",
            industryHint: "  IT  ",
            depth: TraceDepth.Quick,
            callbackUrl: null,
            source: "ui");

        sut.CompanyName.Should().Be("Acme");
        sut.Phone.Should().Be("+420");
        sut.Email.Should().Be("info@acme.cz");
        sut.City.Should().Be("Praha");
        sut.Country.Should().Be("CZ");
        sut.RegistrationId.Should().Be("123");
        sut.TaxId.Should().Be("CZ123");
        sut.IndustryHint.Should().Be("IT");
    }

    [Fact]
    public void Constructor_NullSource_ThrowsArgumentException()
    {
        var act = () => CreateSut(source: null!);
        act.Should().Throw<ArgumentException>().WithParameterName("source");
    }

    [Fact]
    public void Constructor_EmptySource_ThrowsArgumentException()
    {
        var act = () => CreateSut(source: "");
        act.Should().Throw<ArgumentException>().WithParameterName("source");
    }

    [Fact]
    public void Constructor_WhitespaceSource_ThrowsArgumentException()
    {
        var act = () => CreateSut(source: "   ");
        act.Should().Throw<ArgumentException>().WithParameterName("source");
    }

    [Fact]
    public void Constructor_AllIdentifyingFieldsNull_ThrowsArgumentException()
    {
        var act = () => new TraceRequest(
            companyName: null,
            phone: null,
            email: null,
            website: null,
            address: null,
            city: null,
            country: null,
            registrationId: null,
            taxId: null,
            industryHint: null,
            depth: TraceDepth.Quick,
            callbackUrl: null,
            source: "service-bus");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*identifying field*");
    }

    [Fact]
    public void Constructor_OnlyRegistrationId_Succeeds()
    {
        var sut = new TraceRequest(
            companyName: null,
            phone: null,
            email: null,
            website: null,
            address: null,
            city: null,
            country: null,
            registrationId: "12345678",
            taxId: null,
            industryHint: null,
            depth: TraceDepth.Quick,
            callbackUrl: null,
            source: "service-bus");

        sut.RegistrationId.Should().Be("12345678");
        sut.CompanyName.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithCallbackUrl_StoresUri()
    {
        var uri = new Uri("https://callback.example.com/hook");
        var sut = new TraceRequest(
            companyName: "Test",
            phone: null, email: null, website: null, address: null,
            city: null, country: null, registrationId: null, taxId: null,
            industryHint: null,
            depth: TraceDepth.Quick,
            callbackUrl: uri,
            source: "rest-api");

        sut.CallbackUrl.Should().Be(uri);
    }

    [Fact]
    public void Constructor_HttpCallbackUrl_ThrowsArgumentException()
    {
        var act = () => new TraceRequest(
            companyName: "Test",
            phone: null, email: null, website: null, address: null,
            city: null, country: null, registrationId: null, taxId: null,
            industryHint: null,
            depth: TraceDepth.Quick,
            callbackUrl: new Uri("http://insecure.example.com/hook"),
            source: "rest-api");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("callbackUrl")
            .WithMessage("*HTTPS*");
    }

    // ── MarkInProgress ──────────────────────────────────────────────

    [Fact]
    public void MarkInProgress_FromPending_SetsStatusToInProgress()
    {
        var sut = CreateSut();

        sut.MarkInProgress();

        sut.Status.Should().Be(TraceStatus.InProgress);
    }

    [Theory]
    [InlineData(TraceStatus.InProgress)]
    [InlineData(TraceStatus.Completed)]
    [InlineData(TraceStatus.PartiallyCompleted)]
    [InlineData(TraceStatus.Failed)]
    [InlineData(TraceStatus.Cancelled)]
    public void MarkInProgress_FromNonPendingState_ThrowsInvalidOperationException(TraceStatus targetStatus)
    {
        var sut = CreateSut();
        TransitionTo(sut, targetStatus);

        var act = () => sut.MarkInProgress();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*in-progress*");
    }

    // ── Complete ────────────────────────────────────────────────────

    [Fact]
    public void Complete_FromInProgress_SetsStatusToCompleted()
    {
        var sut = CreateSut();
        sut.MarkInProgress();
        var profileId = Guid.NewGuid();
        var confidence = Confidence.Create(0.85);

        sut.Complete(profileId, confidence);

        sut.Status.Should().Be(TraceStatus.Completed);
        sut.CompanyProfileId.Should().Be(profileId);
        sut.OverallConfidence.Should().Be(confidence);
        sut.CompletedAt.Should().NotBeNull();
        sut.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Complete_WithPartialFlag_SetsStatusToPartiallyCompleted()
    {
        var sut = CreateSut();
        sut.MarkInProgress();

        sut.Complete(Guid.NewGuid(), Confidence.Create(0.4), isPartial: true);

        sut.Status.Should().Be(TraceStatus.PartiallyCompleted);
    }

    [Fact]
    public void Complete_RaisesTraceCompletedEvent()
    {
        var sut = CreateSut();
        sut.MarkInProgress();
        var profileId = Guid.NewGuid();
        var confidence = Confidence.Create(0.9);

        sut.Complete(profileId, confidence);

        sut.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TraceCompletedEvent>()
            .Which.Should().BeEquivalentTo(new
            {
                TraceRequestId = sut.Id,
                CompanyProfileId = (Guid?)profileId,
                Status = TraceStatus.Completed,
                OverallConfidence = (Confidence?)confidence,
            });
    }

    [Fact]
    public void Complete_FromPending_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();

        var act = () => sut.Complete(Guid.NewGuid(), Confidence.Create(0.5));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*complete*");
    }

    // ── Fail ────────────────────────────────────────────────────────

    [Fact]
    public void Fail_FromInProgress_SetsStatusToFailed()
    {
        var sut = CreateSut();
        sut.MarkInProgress();

        sut.Fail("All providers returned errors.");

        sut.Status.Should().Be(TraceStatus.Failed);
        sut.FailureReason.Should().Be("All providers returned errors.");
        sut.CompletedAt.Should().NotBeNull();
        sut.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        sut.CompanyProfileId.Should().BeNull();
        sut.OverallConfidence.Should().BeNull();
    }

    [Fact]
    public void Fail_RaisesTraceCompletedEventWithNullProfile()
    {
        var sut = CreateSut();
        sut.MarkInProgress();

        sut.Fail("Timeout");

        sut.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TraceCompletedEvent>()
            .Which.Should().BeEquivalentTo(new
            {
                TraceRequestId = sut.Id,
                CompanyProfileId = (Guid?)null,
                Status = TraceStatus.Failed,
                OverallConfidence = (Confidence?)null,
            });
    }

    [Fact]
    public void Fail_FromPending_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();

        var act = () => sut.Fail("reason");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*fail*");
    }

    [Fact]
    public void Fail_NullReason_ThrowsArgumentException()
    {
        var sut = CreateSut();
        sut.MarkInProgress();

        var act = () => sut.Fail(null!);

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Fail_EmptyReason_ThrowsArgumentException()
    {
        var sut = CreateSut();
        sut.MarkInProgress();

        var act = () => sut.Fail("");

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Fail_LongReason_TruncatesTo2000Characters()
    {
        var sut = CreateSut();
        sut.MarkInProgress();
        var longReason = new string('x', 5000);

        sut.Fail(longReason);

        sut.FailureReason.Should().HaveLength(2000);
    }

    [Fact]
    public void Fail_FromPending_WithNullReason_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();

        var act = () => sut.Fail(null!);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*fail*");
    }

    // ── Cancel ──────────────────────────────────────────────────────

    [Fact]
    public void Cancel_FromPending_SetsStatusToCancelled()
    {
        var sut = CreateSut();

        sut.Cancel();

        sut.Status.Should().Be(TraceStatus.Cancelled);
        sut.CompletedAt.Should().NotBeNull();
        sut.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void Cancel_FromInProgress_SetsStatusToCancelled()
    {
        var sut = CreateSut();
        sut.MarkInProgress();

        sut.Cancel();

        sut.Status.Should().Be(TraceStatus.Cancelled);
    }

    [Theory]
    [InlineData(TraceStatus.Completed)]
    [InlineData(TraceStatus.Failed)]
    [InlineData(TraceStatus.Cancelled)]
    public void Cancel_FromTerminalState_ThrowsInvalidOperationException(TraceStatus targetStatus)
    {
        var sut = CreateSut();
        TransitionTo(sut, targetStatus);

        var act = () => sut.Cancel();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cancel*");
    }

    // ── DurationMs ──────────────────────────────────────────────────

    [Fact]
    public void DurationMs_AfterCompletion_IsNonNegative()
    {
        var sut = CreateSut();
        sut.MarkInProgress();

        sut.Complete(Guid.NewGuid(), Confidence.Create(0.7));

        sut.DurationMs.Should().NotBeNull();
        sut.DurationMs!.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static void TransitionTo(TraceRequest request, TraceStatus status)
    {
        switch (status)
        {
            case TraceStatus.Pending:
                break;
            case TraceStatus.InProgress:
                request.MarkInProgress();
                break;
            case TraceStatus.Completed:
                request.MarkInProgress();
                request.Complete(Guid.NewGuid(), Confidence.Create(0.9));
                break;
            case TraceStatus.PartiallyCompleted:
                request.MarkInProgress();
                request.Complete(Guid.NewGuid(), Confidence.Create(0.3), isPartial: true);
                break;
            case TraceStatus.Failed:
                request.MarkInProgress();
                request.Fail("Test failure");
                break;
            case TraceStatus.Cancelled:
                request.Cancel();
                break;
        }
    }
}
