using FluentAssertions;
using NSubstitute;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services;

public sealed class EntityResolverTests
{
    private readonly ICompanyProfileRepository _repo = Substitute.For<ICompanyProfileRepository>();
    private readonly ICompanyNameNormalizer _normalizer = new CompanyNameNormalizer();
    private readonly IFuzzyNameMatcher _fuzzyMatcher = new FuzzyNameMatcher();
    private readonly ILlmDisambiguator _disambiguator = Substitute.For<ILlmDisambiguator>();

    private EntityResolver CreateSut() => new(_repo, _normalizer, _fuzzyMatcher, _disambiguator);

    // ── Test helpers ────────────────────────────────────────────────

    private static CompanyProfile ProfileWithName(
        string normalizedKey,
        string country,
        string legalName,
        string? registrationId = null,
        int traceCount = 0)
    {
        var profile = new CompanyProfile(normalizedKey, country, registrationId);
        profile.UpdateField(FieldName.LegalName,
            new TracedField<string>
            {
                Value = legalName,
                Confidence = Confidence.Create(0.9),
                Source = "test",
                EnrichedAt = DateTimeOffset.UtcNow,
            },
            "test");

        for (var i = 0; i < traceCount; i++)
            profile.IncrementTraceCount();

        return profile;
    }

    // ── NormalizeName ───────────────────────────────────────────────

    [Fact]
    public void NormalizeName_SkodaAutoVariants_ProduceSameResult()
    {
        var v1 = EntityResolver.NormalizeName("ŠKODA AUTO a.s.");
        var v2 = EntityResolver.NormalizeName("Škoda Auto, a.s.");
        var v3 = EntityResolver.NormalizeName("škoda auto a.s");

        v1.Should().Be(v2);
        v2.Should().Be(v3);
    }

    [Fact]
    public void NormalizeName_RemovesDiacritics()
    {
        var result = EntityResolver.NormalizeName("Průmyslové závody Černošice");

        result.Should().NotContain("Ů");
        result.Should().NotContain("Č");
        result.Should().Contain("PRUMYSLOVE");
    }

    [Fact]
    public void NormalizeName_RemovesLegalForms()
    {
        EntityResolver.NormalizeName("Acme GmbH").Should().NotContain("GMBH");
        EntityResolver.NormalizeName("Test LLC").Should().NotContain("LLC");
        EntityResolver.NormalizeName("Firma Ltd.").Should().NotContain("LTD");
        EntityResolver.NormalizeName("Corp s.r.o.").Should().NotContain("SRO");
    }

    [Fact]
    public void NormalizeName_SortsTokens()
    {
        var v1 = EntityResolver.NormalizeName("Alpha Beta");
        var v2 = EntityResolver.NormalizeName("Beta Alpha");

        v1.Should().Be(v2);
    }

    [Fact]
    public void NormalizeName_RemovesPunctuation()
    {
        var result = EntityResolver.NormalizeName("Acme, Inc. (CZ)");

        result.Should().NotContain(",");
        result.Should().NotContain("(");
    }

    // ── GenerateNormalizedKey ────────────────────────────────────────

    [Fact]
    public void GenerateNormalizedKey_WithRegistrationId_ReturnsCountryColon()
    {
        var sut = CreateSut();

        var key = sut.GenerateNormalizedKey("Acme", "CZ", "12345678");

        key.Should().Be("CZ:12345678");
    }

    [Fact]
    public void GenerateNormalizedKey_WithNameOnly_ReturnsNameHash()
    {
        var sut = CreateSut();

        var key = sut.GenerateNormalizedKey("Škoda Auto a.s.", "CZ", null);

        key.Should().StartWith("NAME:CZ:");
        key.Should().HaveLength("NAME:CZ:".Length + 16); // 16 hex chars
    }

    [Fact]
    public void GenerateNormalizedKey_SameNameDifferentForm_SameHash()
    {
        var sut = CreateSut();

        var key1 = sut.GenerateNormalizedKey("ŠKODA AUTO a.s.", "CZ", null);
        var key2 = sut.GenerateNormalizedKey("Škoda Auto, a.s.", "CZ", null);

        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateNormalizedKey_NoNameNoId_ReturnsUnknown()
    {
        var sut = CreateSut();

        var key = sut.GenerateNormalizedKey(null, "CZ", null);

        key.Should().StartWith("UNKNOWN:");
    }

    // ── ResolveAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ByRegistrationId_ReturnsExisting()
    {
        var sut = CreateSut();
        var existing = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        _repo.FindByRegistrationIdAsync("12345678", "CZ", Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            RegistrationId = "12345678",
            Country = "CZ",
            CompanyName = "Acme",
        }, CancellationToken.None);

        result.Should().Be(existing);
    }

    [Fact]
    public async Task ResolveAsync_ByNormalizedKey_ReturnsExisting()
    {
        var sut = CreateSut();
        var key = sut.GenerateNormalizedKey("Škoda Auto", "CZ", null);
        var existing = new CompanyProfile(key, "CZ");
        _repo.FindByKeyAsync(key, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "ŠKODA AUTO a.s.",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(existing);
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_ReturnsNull()
    {
        var sut = CreateSut();
        _repo.ListByCountryAsync("GB", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompanyProfile>());

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "NonExistent Corp",
            Country = "GB",
        }, CancellationToken.None);

        result.Should().BeNull();
    }

    // ── ResolveAsync — fuzzy fallback (B-63) ─────────────────────────

    [Fact]
    public async Task ResolveAsync_FuzzyMatchAboveThreshold_ReturnsProfile()
    {
        // Existing profile in CKB with a slightly different name.
        var existing = ProfileWithName(
            normalizedKey: "NAME:CZ:abc123",
            country: "CZ",
            legalName: "KOVÁŘOVA FARMA s.r.o.");

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Kovářova farma",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(existing, "fuzzy score between these two names is ≥ 0.85");
    }

    [Fact]
    public async Task ResolveAsync_FuzzyMatchBelowThreshold_ReturnsNull()
    {
        // Existing profile is a completely different company.
        var existing = ProfileWithName(
            normalizedKey: "NAME:CZ:xyz999",
            country: "CZ",
            legalName: "TOTALLY DIFFERENT COMPANY");

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Acme Industries",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().BeNull("fuzzy score is below 0.85");
    }

    [Fact]
    public async Task ResolveAsync_FuzzyMatch_SkippedWhenCountryMissing()
    {
        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Kovářova farma",
            Country = null,
        }, CancellationToken.None);

        result.Should().BeNull();
        await _repo.DidNotReceive().ListByCountryAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_FuzzyMatch_PicksHighestScoringCandidate()
    {
        // Query "Kovářova farma" should match the closely-named candidate, not the unrelated one.
        // Normalization removes "s.r.o." from the strong candidate so it matches the query exactly.
        var weak = ProfileWithName("NAME:CZ:weak", "CZ", "UNRELATED BUSINESS");
        var strong = ProfileWithName("NAME:CZ:strong", "CZ", "KOVÁŘOVA FARMA s.r.o.");

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { weak, strong });

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Kovářova farma",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(strong);
    }

    [Fact]
    public async Task ResolveAsync_ExactMatch_SkipsFuzzySearch()
    {
        // Exact RegistrationId match should short-circuit before loading fuzzy candidates.
        var existing = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        _repo.FindByRegistrationIdAsync("12345678", "CZ", Arg.Any<CancellationToken>())
            .Returns(existing);

        var sut = CreateSut();

        await sut.ResolveAsync(new TraceRequestDto
        {
            RegistrationId = "12345678",
            Country = "CZ",
            CompanyName = "Acme",
        }, CancellationToken.None);

        await _repo.DidNotReceive().ListByCountryAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── FindCandidatesAsync (for B-64) ───────────────────────────────

    [Fact]
    public async Task FindCandidatesAsync_ReturnsCandidatesAboveMinScore_SortedByScoreDesc()
    {
        // Query "Siemens" matches the "SIEMENS" single-token candidate exactly (both normalize to "SIEMENS"),
        // partially matches "SIEMENS HEALTHCARE" (Jaccard 0.5), and not at all against the unrelated one.
        var exact = ProfileWithName("NAME:CZ:a", "CZ", "SIEMENS");
        var partial = ProfileWithName("NAME:CZ:b", "CZ", "SIEMENS HEALTHCARE");
        var irrelevant = ProfileWithName("NAME:CZ:c", "CZ", "UNRELATED CORP");

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { irrelevant, partial, exact });

        var sut = CreateSut();

        var candidates = await sut.FindCandidatesAsync(new TraceRequestDto
        {
            CompanyName = "Siemens",
            Country = "CZ",
        }, maxCandidates: 5, minScore: 0.40, CancellationToken.None);

        candidates.Should().NotBeEmpty();
        candidates.Should().BeInDescendingOrder(c => c.Score);
        candidates.All(c => c.Score >= 0.40).Should().BeTrue();
        candidates[0].Profile.Should().Be(exact);
    }

    [Fact]
    public async Task FindCandidatesAsync_RespectsMaxCandidatesCap()
    {
        var profiles = new[]
        {
            ProfileWithName("NAME:CZ:1", "CZ", "ACME CORP ONE"),
            ProfileWithName("NAME:CZ:2", "CZ", "ACME CORP TWO"),
            ProfileWithName("NAME:CZ:3", "CZ", "ACME CORP THREE"),
            ProfileWithName("NAME:CZ:4", "CZ", "ACME CORP FOUR"),
        };

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(profiles);

        var sut = CreateSut();

        var candidates = await sut.FindCandidatesAsync(new TraceRequestDto
        {
            CompanyName = "Acme Corp",
            Country = "CZ",
        }, maxCandidates: 2, minScore: 0.0, CancellationToken.None);

        candidates.Should().HaveCount(2);
    }

    [Fact]
    public async Task FindCandidatesAsync_NoCountry_ReturnsEmpty()
    {
        var sut = CreateSut();

        var candidates = await sut.FindCandidatesAsync(new TraceRequestDto
        {
            CompanyName = "Some Corp",
            Country = null,
        }, maxCandidates: 5, minScore: 0.70, CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task FindCandidatesAsync_NoName_ReturnsEmpty()
    {
        var sut = CreateSut();

        var candidates = await sut.FindCandidatesAsync(new TraceRequestDto
        {
            CompanyName = null,
            Country = "CZ",
        }, maxCandidates: 5, minScore: 0.70, CancellationToken.None);

        candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task FindCandidatesAsync_InvalidMaxCandidates_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.FindCandidatesAsync(new TraceRequestDto
        {
            CompanyName = "Acme",
            Country = "CZ",
        }, maxCandidates: 0, minScore: 0.70, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task FindCandidatesAsync_SkipsCandidatesWithoutLegalName()
    {
        // A profile without a LegalName field (e.g. enrichment failed) must be skipped,
        // not crash the scoring loop with a null dereference.
        var noName = new CompanyProfile("NAME:CZ:noname", "CZ"); // No LegalName set
        var withName = ProfileWithName("NAME:CZ:named", "CZ", "ACME CORP");

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { noName, withName });

        var sut = CreateSut();

        var candidates = await sut.FindCandidatesAsync(new TraceRequestDto
        {
            CompanyName = "Acme",
            Country = "CZ",
        }, maxCandidates: 10, minScore: 0.0, CancellationToken.None);

        candidates.Should().OnlyContain(c => c.Profile.LegalName != null);
    }

    [Fact]
    public async Task FindCandidatesAsync_NullInput_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.FindCandidatesAsync(null!, 5, 0.70, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── ResolveAsync — LLM disambiguation fallback (B-64) ───────────────────

    [Fact]
    public async Task ResolveAsync_MidTierCandidates_InvokesLlmDisambiguator()
    {
        // Query "Acme Holding Group" (→ tokens: ACME GROUP HOLDING) vs candidate "ACME HOLDING" (→ ACME HOLDING).
        // Jaccard = 2/3 ≈ 0.667; JW on sorted strings is also moderate. Combined score lands in 0.70–0.85.
        var candidate = ProfileWithName("NAME:CZ:m1", "CZ", "ACME HOLDING");
        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { candidate });

        _disambiguator.PickBestMatchAsync(
                Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<FuzzyMatchCandidate>>(),
                Arg.Any<CancellationToken>())
            .Returns(candidate);

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Acme Holding Group",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(candidate);

        await _disambiguator.Received(1).PickBestMatchAsync(
            "Acme Holding Group", "CZ",
            Arg.Is<IReadOnlyList<FuzzyMatchCandidate>>(list => list.Count > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_HighConfidenceFuzzyMatch_SkipsLlmDisambiguator()
    {
        // Candidate normalizes to the same tokens as the query → fuzzy score 1.0 → auto-match.
        var exactMatch = ProfileWithName("NAME:CZ:exact", "CZ", "KOVÁŘOVA FARMA s.r.o.");
        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { exactMatch });

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Kovářova farma",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(exactMatch);
        await _disambiguator.DidNotReceive().PickBestMatchAsync(
            Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<FuzzyMatchCandidate>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_NoMidTierCandidates_DoesNotInvokeLlm()
    {
        // Candidate is unrelated — fuzzy score will be well below 0.70.
        var unrelated = ProfileWithName("NAME:CZ:unrelated", "CZ", "TOTALLY DIFFERENT COMPANY");
        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { unrelated });

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Acme",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().BeNull();
        await _disambiguator.DidNotReceive().PickBestMatchAsync(
            Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<FuzzyMatchCandidate>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_LlmReturnsNull_ResolveAsyncReturnsNull()
    {
        // Ambiguous candidate → fuzzy in 0.70–0.85 → LLM called but declines.
        var candidate = ProfileWithName("NAME:CZ:m1", "CZ", "ACME HOLDING");
        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { candidate });

        _disambiguator.PickBestMatchAsync(
                Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<IReadOnlyList<FuzzyMatchCandidate>>(),
                Arg.Any<CancellationToken>())
            .Returns((CompanyProfile?)null);

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Acme Holding Group",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().BeNull();
        await _disambiguator.Received(1).PickBestMatchAsync(
            Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<IReadOnlyList<FuzzyMatchCandidate>>(),
            Arg.Any<CancellationToken>());
    }

    // ── ResolveAsync — B-95 two-pass blocking ─────────────────────────

    [Fact]
    public async Task ResolveAsync_HighConfidenceInHotSet_SkipsWiderScan()
    {
        // Pass 1 (4-arg overload, minTraceCount filter) returns a hot match that scores ≥ 0.85.
        // Pass 2 (3-arg overload, full pool) must NOT be invoked.
        var hot = ProfileWithName(
            normalizedKey: "NAME:CZ:hot",
            country: "CZ",
            legalName: "KOVÁŘOVA FARMA s.r.o.",
            traceCount: 50);

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { hot });

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Kovářova farma",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(hot);
        await _repo.DidNotReceive().ListByCountryAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_HotSetEmpty_FallsBackToWiderScan()
    {
        // Pass 1 finds nothing (no hot profiles match), pass 2 picks up the candidate.
        var cold = ProfileWithName(
            normalizedKey: "NAME:CZ:cold",
            country: "CZ",
            legalName: "KOVÁŘOVA FARMA s.r.o.",
            traceCount: 0);

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompanyProfile>());
        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { cold });

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Kovářova farma",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(cold);
        await _repo.Received(1).ListByCountryAsync(
            "CZ", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).ListByCountryAsync(
            "CZ", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_HotSetNotConfident_DedupesWithWiderScan()
    {
        // Hot profile has insufficient match (< 0.85); pass 2 includes the same profile by ID
        // plus another. Resolver must not double-score the hot profile.
        var hot = ProfileWithName(
            normalizedKey: "NAME:CZ:hot",
            country: "CZ",
            legalName: "ACME HOLDING",
            traceCount: 50);
        var strongMatch = ProfileWithName(
            normalizedKey: "NAME:CZ:strong",
            country: "CZ",
            legalName: "KOVÁŘOVA FARMA s.r.o.",
            traceCount: 0);

        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { hot });
        _repo.ListByCountryAsync("CZ", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { hot, strongMatch });

        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "Kovářova farma",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(strongMatch);
    }
}
