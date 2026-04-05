using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Services;

public sealed class WaterfallOrchestratorTests
{
    private readonly ICompanyProfileRepository _profileRepo = Substitute.For<ICompanyProfileRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ILogger<WaterfallOrchestrator> _logger = Substitute.For<ILogger<WaterfallOrchestrator>>();

    private WaterfallOrchestrator CreateSut(params IEnrichmentProvider[] providers) =>
        new(providers, _profileRepo, _unitOfWork, _logger);

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

        await _profileRepo.Received(1).UpsertAsync(Arg.Any<CompanyProfile>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsProfileWithConfidence()
    {
        var provider = CreateMockProvider("ares", 10, canHandle: true);
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
}
