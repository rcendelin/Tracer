using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="LlmDisambiguator"/> — orchestrates the LLM client, applies the
/// 0.7 confidence discount, and enforces the 0.5 match threshold.
/// </summary>
public sealed class LlmDisambiguatorTests
{
    private readonly ILlmDisambiguatorClient _client = Substitute.For<ILlmDisambiguatorClient>();

    private LlmDisambiguator CreateSut() =>
        new(_client, NullLogger<LlmDisambiguator>.Instance);

    // ── Test helpers ────────────────────────────────────────────────────────

    private static CompanyProfile ProfileNamed(string normalizedKey, string legalName) =>
        new CompanyProfile(normalizedKey, "CZ").Also(p =>
            p.UpdateField(FieldName.LegalName,
                new TracedField<string>
                {
                    Value = legalName,
                    Confidence = Confidence.Create(0.9),
                    Source = "test",
                    EnrichedAt = DateTimeOffset.UtcNow,
                },
                "test"));

    private static List<FuzzyMatchCandidate> Candidates(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new FuzzyMatchCandidate(
                ProfileNamed($"NAME:CZ:c{i}", $"CANDIDATE {i}"),
                0.70 + (i * 0.01)))
            .ToList();

    // ── Empty / disabled paths ──────────────────────────────────────────────

    [Fact]
    public async Task PickBestMatchAsync_EmptyCandidates_ReturnsNullWithoutCallingClient()
    {
        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", [], CancellationToken.None);

        result.Should().BeNull();
        await _client.DidNotReceive().DisambiguateAsync(
            Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PickBestMatchAsync_ClientReturnsNull_ReturnsNull()
    {
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns((DisambiguationResponse?)null);

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", Candidates(3), CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Index = -1 (explicit no match) ──────────────────────────────────────

    [Fact]
    public async Task PickBestMatchAsync_IndexNegativeOne_ReturnsNull()
    {
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(-1, 1.0, "none match"));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", Candidates(3), CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Calibration math: calibrated = raw × 0.7 ────────────────────────────

    [Fact]
    public async Task PickBestMatchAsync_RawConfidenceOne_ReturnsCandidate()
    {
        // calibrated = 1.0 × 0.7 = 0.7 → ≥ 0.5 threshold → match
        var candidates = Candidates(3);
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(1, 1.0, "perfect match"));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", candidates, CancellationToken.None);

        result.Should().Be(candidates[1].Profile);
    }

    [Fact]
    public async Task PickBestMatchAsync_RawBelowThreshold_ReturnsNull()
    {
        // calibrated = 0.6 × 0.7 = 0.42 → < 0.5 → no match
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(0, 0.6, "uncertain"));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", Candidates(3), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PickBestMatchAsync_RawAtBorderline_ReturnsCandidate()
    {
        // calibrated = 0.72 × 0.7 = 0.504 → ≥ 0.5 → match
        var candidates = Candidates(3);
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(0, 0.72, "likely"));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", candidates, CancellationToken.None);

        result.Should().Be(candidates[0].Profile);
    }

    [Fact]
    public async Task PickBestMatchAsync_RawJustBelowBorderline_ReturnsNull()
    {
        // calibrated = 0.71 × 0.7 = 0.497 → < 0.5 → no match
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(0, 0.71, "weak"));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", Candidates(3), CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Defensive clamping ──────────────────────────────────────────────────

    [Fact]
    public async Task PickBestMatchAsync_NegativeConfidence_TreatedAsZero_ReturnsNull()
    {
        // clamped to 0 → calibrated 0 → < 0.5 → no match
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(0, -0.5, null));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", Candidates(3), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PickBestMatchAsync_AboveOneConfidence_ClampedAndMatches()
    {
        // 1.5 clamped to 1.0 → calibrated 0.7 → ≥ 0.5 → match
        var candidates = Candidates(3);
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(2, 1.5, "over-confident"));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", candidates, CancellationToken.None);

        result.Should().Be(candidates[2].Profile);
    }

    [Fact]
    public async Task PickBestMatchAsync_OutOfRangeIndex_ReturnsNull()
    {
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(99, 0.9, "bad index"));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", Candidates(3), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task PickBestMatchAsync_NegativeIndexOtherThanMinusOne_ReturnsNull()
    {
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new DisambiguationResponse(-5, 0.9, "unusual"));

        var sut = CreateSut();

        var result = await sut.PickBestMatchAsync(
            "Acme", "CZ", Candidates(3), CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Candidate list capping ──────────────────────────────────────────────

    [Fact]
    public async Task PickBestMatchAsync_CapsCandidatesAtFive()
    {
        var candidates = Candidates(10);

        DisambiguationRequest? captured = null;
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<DisambiguationRequest>();
                return Task.FromResult<DisambiguationResponse?>(
                    new DisambiguationResponse(-1, 0.0, null));
            });

        var sut = CreateSut();
        await sut.PickBestMatchAsync("Acme", "CZ", candidates, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Candidates.Count.Should().Be(5, "MaxCandidates constant caps at 5");
    }

    [Fact]
    public async Task PickBestMatchAsync_ThreeCandidates_PassesAllThree()
    {
        var candidates = Candidates(3);

        DisambiguationRequest? captured = null;
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<DisambiguationRequest>();
                return Task.FromResult<DisambiguationResponse?>(
                    new DisambiguationResponse(-1, 0.0, null));
            });

        var sut = CreateSut();
        await sut.PickBestMatchAsync("Acme", "CZ", candidates, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Candidates.Count.Should().Be(3);
    }

    // ── Input validation ────────────────────────────────────────────────────

    [Fact]
    public async Task PickBestMatchAsync_NullQueryName_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.PickBestMatchAsync(null!, "CZ", Candidates(1), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PickBestMatchAsync_WhitespaceQueryName_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.PickBestMatchAsync("   ", "CZ", Candidates(1), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PickBestMatchAsync_NullCandidates_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.PickBestMatchAsync("Acme", "CZ", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PickBestMatchAsync_CancellationToken_PropagatedToClient()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken captured = default;
        _client.DisambiguateAsync(Arg.Any<DisambiguationRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                captured = callInfo.Arg<CancellationToken>();
                return Task.FromResult<DisambiguationResponse?>(null);
            });

        var sut = CreateSut();
        await sut.PickBestMatchAsync("Acme", "CZ", Candidates(1), cts.Token);

        captured.Should().Be(cts.Token);
    }
}

/// <summary>Small fluent helper to initialise CompanyProfile in a single expression.</summary>
internal static class CompanyProfileTestExtensions
{
    public static CompanyProfile Also(this CompanyProfile profile, Action<CompanyProfile> action)
    {
        action(profile);
        return profile;
    }
}
