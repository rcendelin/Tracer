using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services;

public sealed class CkbPersistenceServiceTests
{
    private readonly ICompanyProfileRepository _profileRepo = Substitute.For<ICompanyProfileRepository>();
    private readonly IChangeEventRepository _changeEventRepo = Substitute.For<IChangeEventRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IConfidenceScorer _scorer = new ConfidenceScorer();
    private readonly IProfileCacheService _cache = Substitute.For<IProfileCacheService>();
    private readonly ILogger<CkbPersistenceService> _logger = Substitute.For<ILogger<CkbPersistenceService>>();

    private CkbPersistenceService CreateSut() =>
        new(_profileRepo, _changeEventRepo, _unitOfWork, _scorer, _cache, _logger);

    private static CompanyProfile CreateProfile() => new("CZ:12345678", "CZ", "12345678");

    private static MergeResult CreateMergeResult(Dictionary<FieldName, TracedField<object>>? bestFields = null) =>
        new()
        {
            BestFields = bestFields ?? new Dictionary<FieldName, TracedField<object>>
            {
                [FieldName.LegalName] = new TracedField<object>
                {
                    Value = "Acme s.r.o.",
                    Confidence = Confidence.Create(0.9),
                    Source = "ares",
                    EnrichedAt = DateTimeOffset.UtcNow,
                },
            },
            CandidateValues = new Dictionary<FieldName, IReadOnlyCollection<TracedField<object>>>(),
        };

    [Fact]
    public async Task PersistEnrichmentAsync_UpsertsProfile()
    {
        var sut = CreateSut();
        var profile = CreateProfile();

        await sut.PersistEnrichmentAsync(
            profile,
            [],
            CreateMergeResult(),
            Guid.NewGuid(),
            CancellationToken.None);

        await _profileRepo.Received(1).UpsertAsync(profile, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistEnrichmentAsync_SetsOverallConfidence()
    {
        var sut = CreateSut();
        var profile = CreateProfile();

        await sut.PersistEnrichmentAsync(
            profile,
            [],
            CreateMergeResult(),
            Guid.NewGuid(),
            CancellationToken.None);

        profile.OverallConfidence.Should().NotBeNull();
        profile.TraceCount.Should().Be(1);
    }

    [Fact]
    public async Task PersistEnrichmentAsync_NewField_CreatesChangeEvent()
    {
        var sut = CreateSut();
        var profile = CreateProfile();

        await sut.PersistEnrichmentAsync(
            profile,
            [],
            CreateMergeResult(),
            Guid.NewGuid(),
            CancellationToken.None);

        // LegalName is new → ChangeEvent with Created type
        await _changeEventRepo.Received(1).AddAsync(
            Arg.Is<ChangeEvent>(e => e.ChangeType == ChangeType.Created),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistEnrichmentAsync_ChangedField_CreatesUpdateChangeEvent()
    {
        var sut = CreateSut();
        var profile = CreateProfile();

        // Set initial field
        profile.UpdateField(FieldName.LegalName, new TracedField<string>
        {
            Value = "Old Name",
            Confidence = Confidence.Create(0.8),
            Source = "gleif-lei",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-30),
        }, "gleif-lei");

        // Now merge with new value
        var mergeResult = CreateMergeResult(new()
        {
            [FieldName.LegalName] = new TracedField<object>
            {
                Value = "New Name s.r.o.",
                Confidence = Confidence.Create(0.95),
                Source = "ares",
                EnrichedAt = DateTimeOffset.UtcNow,
            },
        });

        await sut.PersistEnrichmentAsync(
            profile,
            [],
            mergeResult,
            Guid.NewGuid(),
            CancellationToken.None);

        await _changeEventRepo.Received(1).AddAsync(
            Arg.Is<ChangeEvent>(e => e.ChangeType == ChangeType.Updated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistEnrichmentAsync_SameValue_NoChangeEvent()
    {
        var sut = CreateSut();
        var profile = CreateProfile();

        // Set initial field
        profile.UpdateField(FieldName.LegalName, new TracedField<string>
        {
            Value = "Acme s.r.o.",
            Confidence = Confidence.Create(0.8),
            Source = "ares",
            EnrichedAt = DateTimeOffset.UtcNow.AddDays(-1),
        }, "ares");

        // Merge with same value
        await sut.PersistEnrichmentAsync(
            profile,
            [],
            CreateMergeResult(),
            Guid.NewGuid(),
            CancellationToken.None);

        await _changeEventRepo.DidNotReceive().AddAsync(
            Arg.Any<ChangeEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PersistEnrichmentAsync_AddressField_HandledCorrectly()
    {
        var sut = CreateSut();
        var profile = CreateProfile();

        var addr = new Address
        {
            Street = "Hlavní 1",
            City = "Praha",
            PostalCode = "11000",
            Country = "CZ",
        };

        var mergeResult = new MergeResult
        {
            BestFields = new Dictionary<FieldName, TracedField<object>>
            {
                [FieldName.RegisteredAddress] = new TracedField<object>
                {
                    Value = addr,
                    Confidence = Confidence.Create(0.9),
                    Source = "ares",
                    EnrichedAt = DateTimeOffset.UtcNow,
                },
            },
            CandidateValues = new Dictionary<FieldName, IReadOnlyCollection<TracedField<object>>>(),
        };

        await sut.PersistEnrichmentAsync(
            profile,
            [],
            mergeResult,
            Guid.NewGuid(),
            CancellationToken.None);

        profile.RegisteredAddress.Should().NotBeNull();
        profile.RegisteredAddress!.Value.City.Should().Be("Praha");
    }
}
