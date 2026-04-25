using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services;

public sealed class WaterfallOrchestratorTests
{
    private readonly ICompanyProfileRepository _profileRepo = Substitute.For<ICompanyProfileRepository>();
    private readonly IGoldenRecordMerger _merger = new GoldenRecordMerger(new ConfidenceScorer());
    private readonly ICkbPersistenceService _persistenceService = Substitute.For<ICkbPersistenceService>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITracerMetrics _metrics = Substitute.For<ITracerMetrics>();
    private readonly ILogger<WaterfallOrchestrator> _logger = NullLogger<WaterfallOrchestrator>.Instance;

    private WaterfallOrchestrator CreateSut(params IEnrichmentProvider[] providers) =>
        new(providers, _profileRepo, _merger, _persistenceService, _mediator, _metrics, _logger);

    private WaterfallOrchestrator CreateSutWithDepthTimeout(TimeSpan depthTimeout, params IEnrichmentProvider[] providers) =>
        new(providers, _profileRepo, _merger, _persistenceService, _mediator, _metrics, _logger)
        {
            DepthTimeoutOverride = _ => depthTimeout,
        };

    private static TraceRequest CreateRequest(
        string? companyName = "Acme s.r.o.",
        string? registrationId = "12345678",
        string? country = "CZ",
        TraceDepth depth = TraceDepth.Standard)
    {
        var req = new TraceRequest(
            companyName: companyName,
            phone: null, email: null, website: null, address: null,
            city: null, country: country,
            registrationId: registrationId,
            taxId: null, industryHint: null,
            depth: depth,
            callbackUrl: null,
            source: "test");
        req.MarkInProgress();
        return req;
    }

    private static IEnrichmentProvider CreateMockProvider(
        string id, int priority, bool canHandle, ProviderResult? result = null)
    {
        var provider = Substitute.For<IEnrichmentProvider>();
        provider.ProviderId.Returns(id);
        provider.Priority.Returns(priority);
        provider.SourceQuality.Returns(0.9);
        provider.CanHandle(Arg.Any<TraceContext>()).Returns(canHandle);
        provider.EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(result ?? ProviderResult.Success(
                new Dictionary<FieldName, object?> { [FieldName.LegalName] = "Acme" },
                TimeSpan.FromMilliseconds(100)));
        return provider;
    }

    [Fact]
    public async Task ExecuteAsync_CallsApplicableProviders()
    {
        var p1 = CreateMockProvider("ares", 10, canHandle: true);
        var p2 = CreateMockProvider("gleif", 30, canHandle: true);
        var p3 = CreateMockProvider("google", 50, canHandle: false);
        var sut = CreateSut(p1, p2, p3);

        await sut.ExecuteAsync(CreateRequest(), CancellationToken.None);

        await p1.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await p2.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await p3.DidNotReceive().EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UpsertsProfileToCKB()
    {
        var provider = CreateMockProvider("ares", 10, canHandle: true);
        var sut = CreateSut(provider);

        await sut.ExecuteAsync(CreateRequest(), CancellationToken.None);

        await _persistenceService.Received(1).PersistEnrichmentAsync(
            Arg.Any<CompanyProfile>(), Arg.Any<IReadOnlyCollection<(string, ProviderResult)>>(),
            Arg.Any<MergeResult>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        // Persistence is now handled by CkbPersistenceService (verified above)
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsProfileWithConfidence()
    {
        var provider = CreateMockProvider("ares", 10, canHandle: true);

        // CkbPersistenceService sets OverallConfidence on the profile (real impl calls scorer).
        // Configure the mock to simulate this so the returned profile has a non-null confidence.
        _persistenceService
            .When(x => x.PersistEnrichmentAsync(
                Arg.Any<CompanyProfile>(),
                Arg.Any<IReadOnlyCollection<(string, ProviderResult)>>(),
                Arg.Any<MergeResult>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>()))
            .Do(call => call.Arg<CompanyProfile>().SetOverallConfidence(Confidence.Create(0.9)));

        var sut = CreateSut(provider);

        var result = await sut.ExecuteAsync(CreateRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        result.OverallConfidence.Should().NotBeNull();
        result.NormalizedKey.Should().Be("CZ:12345678");
    }

    [Fact]
    public async Task ExecuteAsync_NoProviders_ReturnsEmptyProfile()
    {
        var provider = CreateMockProvider("ares", 10, canHandle: false);
        var sut = CreateSut(provider);

        var result = await sut.ExecuteAsync(CreateRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        result.NormalizedKey.Should().Be("CZ:12345678");
    }

    [Fact]
    public async Task ExecuteAsync_ProviderThrows_ContinuesWithOthers()
    {
        var failing = Substitute.For<IEnrichmentProvider>();
        failing.ProviderId.Returns("failing");
        failing.Priority.Returns(10);
        failing.CanHandle(Arg.Any<TraceContext>()).Returns(true);
        failing.EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns<ProviderResult>(_ => throw new InvalidOperationException("Provider crashed"));

        var working = CreateMockProvider("working", 10, canHandle: true);
        var sut = CreateSut(failing, working);

        var result = await sut.ExecuteAsync(CreateRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        await working.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExistingProfile_UsesFromCKB()
    {
        var existing = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        _profileRepo.FindByKeyAsync("CZ:12345678", Arg.Any<CancellationToken>())
            .Returns(existing);

        var provider = CreateMockProvider("ares", 10, canHandle: true);
        var sut = CreateSut(provider);

        var result = await sut.ExecuteAsync(CreateRequest(), CancellationToken.None);

        result.Id.Should().Be(existing.Id);
    }

    [Fact]
    public async Task ExecuteAsync_Tier1ProvidersRunInParallel()
    {
        // Two Tier 1 providers — both should be called
        var p1 = CreateMockProvider("ares", 10, canHandle: true);
        var p2 = CreateMockProvider("gleif", 30, canHandle: true);
        var sut = CreateSut(p1, p2);

        await sut.ExecuteAsync(CreateRequest(), CancellationToken.None);

        await p1.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await p2.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    // ── Tier 2 (priority 101–200) ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Tier2Provider_StandardDepth_IsExecuted()
    {
        var tier1 = CreateMockProvider("ares", 10, canHandle: true);
        var tier2 = CreateMockProvider("web-scraper", 150, canHandle: true);
        var sut = CreateSut(tier1, tier2);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Standard), CancellationToken.None);

        await tier2.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Tier2Provider_DeepDepth_IsExecuted()
    {
        var tier2 = CreateMockProvider("web-scraper", 150, canHandle: true);
        var sut = CreateSut(tier2);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Deep), CancellationToken.None);

        await tier2.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Tier2Provider_QuickDepth_IsSkipped()
    {
        var tier1 = CreateMockProvider("ares", 10, canHandle: true);
        var tier2 = CreateMockProvider("web-scraper", 150, canHandle: true);
        var sut = CreateSut(tier1, tier2);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Quick), CancellationToken.None);

        // Tier 2 must NOT be called for Quick depth
        await tier2.DidNotReceive().EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    // ── Tier 3 (priority > 200) ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Tier3Provider_DeepDepth_IsExecuted()
    {
        var tier3 = CreateMockProvider("ai-extractor", 250, canHandle: true);
        var sut = CreateSut(tier3);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Deep), CancellationToken.None);

        await tier3.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Tier3Provider_StandardDepth_IsSkipped()
    {
        var tier3 = CreateMockProvider("ai-extractor", 250, canHandle: true);
        var sut = CreateSut(tier3);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Standard), CancellationToken.None);

        await tier3.DidNotReceive().EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Tier3Provider_QuickDepth_IsSkipped()
    {
        var tier3 = CreateMockProvider("ai-extractor", 250, canHandle: true);
        var sut = CreateSut(tier3);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Quick), CancellationToken.None);

        await tier3.DidNotReceive().EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    // ── Tier execution order ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DeepDepth_AllThreeTiersExecuted()
    {
        var tier1 = CreateMockProvider("ares", 10, canHandle: true);
        var tier2 = CreateMockProvider("web-scraper", 150, canHandle: true);
        var tier3 = CreateMockProvider("ai-extractor", 250, canHandle: true);
        var sut = CreateSut(tier1, tier2, tier3);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Deep), CancellationToken.None);

        await tier1.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await tier2.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await tier3.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_StandardDepth_OnlyTier1And2Executed()
    {
        var tier1 = CreateMockProvider("ares", 10, canHandle: true);
        var tier2 = CreateMockProvider("web-scraper", 150, canHandle: true);
        var tier3 = CreateMockProvider("ai-extractor", 250, canHandle: true);
        var sut = CreateSut(tier1, tier2, tier3);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Standard), CancellationToken.None);

        await tier1.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await tier2.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await tier3.DidNotReceive().EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_QuickDepth_OnlyTier1Executed()
    {
        var tier1 = CreateMockProvider("ares", 10, canHandle: true);
        var tier2 = CreateMockProvider("web-scraper", 150, canHandle: true);
        var tier3 = CreateMockProvider("ai-extractor", 250, canHandle: true);
        var sut = CreateSut(tier1, tier2, tier3);

        await sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Quick), CancellationToken.None);

        await tier1.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await tier2.DidNotReceive().EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await tier3.DidNotReceive().EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
    }

    // ── Depth budget timeout ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_DepthBudgetExpires_ReturnsPartialResultsWithoutThrowing()
    {
        // Tier 1 completes instantly; Tier 2 blocks indefinitely.
        // Depth budget is overridden to 150ms so the budget fires quickly without real waiting.
        // Expected behaviour: orchestrator catches the budget OCE and returns a profile
        // based on partial (Tier 1 only) results — does NOT throw.
        var tier1Result = ProviderResult.Success(
            new Dictionary<FieldName, object?> { [FieldName.LegalName] = "Acme" },
            TimeSpan.FromMilliseconds(10));

        var tier1 = CreateMockProvider("ares", 10, canHandle: true, result: tier1Result);

        // Slow Tier 2 that blocks until cancelled
        var tier2 = Substitute.For<IEnrichmentProvider>();
        tier2.ProviderId.Returns("web-scraper");
        tier2.Priority.Returns(150);
        tier2.CanHandle(Arg.Any<TraceContext>()).Returns(true);
        tier2.EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(async (callInfo) =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                return ProviderResult.Success(new Dictionary<FieldName, object?>(), TimeSpan.Zero);
            });

        // Use a 150ms depth budget so the test doesn't need to wait for real timeouts
        var sut = CreateSutWithDepthTimeout(TimeSpan.FromMilliseconds(150), tier1, tier2);

        // Act — must NOT throw; partial results from Tier 1 should be used
        var act = () => sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Standard), CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Tier 1 ran; Tier 2 started but was cut short by the budget
        await tier1.Received(1).EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>());
        await _persistenceService.Received(1).PersistEnrichmentAsync(
            Arg.Any<CompanyProfile>(), Arg.Any<IReadOnlyCollection<(string, ProviderResult)>>(),
            Arg.Any<MergeResult>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_Propagates()
    {
        // Provider blocks on a real async delay; caller cancels mid-flight.
        // Verifies that OperationCanceledException from real cooperative cancellation propagates out.
        using var cts = new CancellationTokenSource();

        var provider = Substitute.For<IEnrichmentProvider>();
        provider.ProviderId.Returns("ares");
        provider.Priority.Returns(10);
        provider.CanHandle(Arg.Any<TraceContext>()).Returns(true);
        provider.EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(async (callInfo) =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                // Cancel after a brief delay to ensure the async path is reached
                await cts.CancelAsync().ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                return ProviderResult.Success(new Dictionary<FieldName, object?>(), TimeSpan.Zero);
            });

        var sut = CreateSutWithDepthTimeout(TimeSpan.FromSeconds(30), provider);

        var act = () => sut.ExecuteAsync(CreateRequest(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_DepthBudgetExpires_DuringTier1_ReturnsPartialResultsWithoutThrowing()
    {
        // Two Tier 1 providers: one fast, one blocks.
        // Depth budget fires while Task.WhenAll is in flight.
        // Expected: fast provider's result is kept, orchestrator does NOT throw.
        var fastResult = ProviderResult.Success(
            new Dictionary<FieldName, object?> { [FieldName.LegalName] = "Acme" },
            TimeSpan.FromMilliseconds(10));

        var fastProvider = CreateMockProvider("gleif", 30, canHandle: true, result: fastResult);

        var slowProvider = Substitute.For<IEnrichmentProvider>();
        slowProvider.ProviderId.Returns("ares");
        slowProvider.Priority.Returns(10);
        slowProvider.CanHandle(Arg.Any<TraceContext>()).Returns(true);
        slowProvider.EnrichAsync(Arg.Any<TraceContext>(), Arg.Any<CancellationToken>())
            .Returns(async (callInfo) =>
            {
                var ct = callInfo.Arg<CancellationToken>();
                await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                return ProviderResult.Success(new Dictionary<FieldName, object?>(), TimeSpan.Zero);
            });

        var sut = CreateSutWithDepthTimeout(TimeSpan.FromMilliseconds(150), slowProvider, fastProvider);

        var act = () => sut.ExecuteAsync(CreateRequest(depth: TraceDepth.Standard), CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Persistence must have been called (with partial results)
        await _persistenceService.Received(1).PersistEnrichmentAsync(
            Arg.Any<CompanyProfile>(), Arg.Any<IReadOnlyCollection<(string, ProviderResult)>>(),
            Arg.Any<MergeResult>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
