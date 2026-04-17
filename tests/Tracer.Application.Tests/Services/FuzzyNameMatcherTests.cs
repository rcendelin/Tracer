using FluentAssertions;
using Tracer.Application.Services;

namespace Tracer.Application.Tests.Services;

/// <summary>
/// Tests for <see cref="FuzzyNameMatcher"/> — Jaro-Winkler (60%) + Token Jaccard (40%) scoring.
/// Inputs are expected to be pre-normalized (uppercase, no punctuation, tokens sorted).
/// </summary>
public sealed class FuzzyNameMatcherTests
{
    private readonly FuzzyNameMatcher _sut = new();
    private readonly CompanyNameNormalizer _normalizer = new();

    // ── Basic properties ─────────────────────────────────────────────────────

    [Fact]
    public void Score_IdenticalStrings_ReturnsOne()
    {
        _sut.Score("ACME CORP", "ACME CORP").Should().Be(1.0);
    }

    [Fact]
    public void Score_SingleChar_Identical_ReturnsOne()
    {
        _sut.Score("A", "A").Should().Be(1.0);
    }

    [Fact]
    public void Score_Symmetric_BothDirectionsEqual()
    {
        var a = "ACME CORP INTERNATIONAL";
        var b = "AMCE CORP INTERNTIONAL";

        _sut.Score(a, b).Should().BeApproximately(_sut.Score(b, a), 1e-10);
    }

    [Fact]
    public void Score_ResultAlwaysInRangeZeroOne()
    {
        var tests = new[]
        {
            ("A", "B"),
            ("ABCDEF", "XYZUVW"),
            ("SHORT", "A"),
            ("ALPHA BETA", "BETA ALPHA"),
            ("ACME", "ACME CORP"),
        };

        foreach (var (a, b) in tests)
        {
            var score = _sut.Score(a, b);
            score.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(1.0,
                $"score for ({a}, {b}) should be in [0,1]");
        }
    }

    [Fact]
    public void Score_CompletelyDifferent_LowScore()
    {
        // No token overlap, no common prefix — should be low.
        _sut.Score("APPLE", "ZYXWVU").Should().BeLessThan(0.5);
    }

    // ── Input validation ─────────────────────────────────────────────────────

    [Fact]
    public void Score_NullFirstArg_Throws()
    {
        var act = () => _sut.Score(null!, "ACME");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Score_NullSecondArg_Throws()
    {
        var act = () => _sut.Score("ACME", null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Score_EmptyFirstArg_Throws()
    {
        var act = () => _sut.Score("", "ACME");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Score_WhitespaceFirstArg_Throws()
    {
        var act = () => _sut.Score("   ", "ACME");
        act.Should().Throw<ArgumentException>();
    }

    // ── Jaro-Winkler behaviour (via combined score) ──────────────────────────

    [Fact]
    public void Score_CommonPrefix_BoostsScore()
    {
        // Jaro-Winkler should boost score for strings sharing a 4-char prefix.
        var sharedPrefix = _sut.Score("ACMEKINGS", "ACMEQUEEN");
        var noSharedPrefix = _sut.Score("ACMEKINGS", "XYZAKINGS");

        sharedPrefix.Should().BeGreaterThan(noSharedPrefix);
    }

    [Fact]
    public void Score_SingleTokenTypo_JaroWinklerContributes()
    {
        // Single-token pairs with char-level typos have Jaccard = 0 (distinct tokens),
        // so the combined score is capped by the JW weight (0.6). The JW component
        // must still contribute meaningfully — at least > 0.5 (0.6 × ~0.94 ≈ 0.56).
        _sut.Score("MICROSOFT", "MIKROSOFT").Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void Score_TranspositionTypo_JaroWinklerContributes()
    {
        // Transposed chars: Jaro handles well, but single-token Jaccard is 0.
        // Combined is still ~0.6 × JW which should exceed 0.5.
        _sut.Score("MARTHA", "MARHTA").Should().BeGreaterThan(0.5);
    }

    // ── Token Jaccard behaviour ──────────────────────────────────────────────

    [Fact]
    public void Score_TokenReordering_ScoresHigh()
    {
        // Token Jaccard is order-independent — reordering tokens keeps the Jaccard component at 1.0.
        var a = "ACME CORP INTERNATIONAL";
        var b = "INTERNATIONAL CORP ACME";

        // Even though JW will be low (strings differ at char level), Jaccard is 1.0,
        // contributing 0.4 to the combined score.
        _sut.Score(a, b).Should().BeGreaterThanOrEqualTo(0.4);
    }

    [Fact]
    public void Score_PartialTokenOverlap_MidScore()
    {
        // "APPLE CORP" and "APPLE BANK" share one token (APPLE) out of 3 unique (APPLE, CORP, BANK).
        // Jaccard = 1/3. JW is also moderate.
        var score = _sut.Score("APPLE CORP", "APPLE BANK");
        score.Should().BeGreaterThan(0.3).And.BeLessThan(0.9);
    }

    [Fact]
    public void Score_NoTokenOverlap_LowerScore()
    {
        // Tokens completely disjoint → Jaccard = 0, only JW contributes.
        var score = _sut.Score("ALPHA BETA", "GAMMA DELTA");
        score.Should().BeLessThan(0.7);
    }

    // ── Real-world company name pairs ────────────────────────────────────────

    [Fact]
    public void Score_KovarovaFarmaVariants_AtLeastHighConfidence()
    {
        // Acceptance criterion from B-63 spec:
        // "Kovářova farma" matches "KOVÁŘOVA FARMA s.r.o." with score ≥ 0.85
        var query = _normalizer.Normalize("Kovářova farma");
        var candidate = _normalizer.Normalize("KOVÁŘOVA FARMA s.r.o.");

        _sut.Score(query, candidate).Should().BeGreaterThanOrEqualTo(0.85);
    }

    [Fact]
    public void Score_SkodaAutoVariants_NearlyPerfect()
    {
        var query = _normalizer.Normalize("Škoda Auto a.s.");
        var candidate = _normalizer.Normalize("SKODA AUTO");

        // Both normalize to the same tokens → score should be 1.0.
        _sut.Score(query, candidate).Should().Be(1.0);
    }

    [Fact]
    public void Score_AppleVsMicrosoft_VeryLow()
    {
        var a = _normalizer.Normalize("Apple Inc");
        var b = _normalizer.Normalize("Microsoft Corporation");

        _sut.Score(a, b).Should().BeLessThan(0.5);
    }

    [Fact]
    public void Score_WithSuffixVariant_ModerateScore()
    {
        // "Siemens Group" → "GROUP SIEMENS" (2 tokens), "Siemens" → "SIEMENS" (1 token).
        // Jaccard = 1/2 = 0.5. JW on the full sorted strings is low (different lengths),
        // so combined ≈ 0.4–0.5. This is below auto-match threshold but above "no match".
        var a = _normalizer.Normalize("Siemens Group");
        var b = _normalizer.Normalize("Siemens");

        _sut.Score(a, b).Should().BeGreaterThan(0.30);
    }

    // ── Combined weighting sanity ────────────────────────────────────────────

    [Fact]
    public void Score_UsesWeightedCombination()
    {
        // For strings with full token overlap (Jaccard = 1.0) but char-level differences,
        // the score must be at least 0.4 (the Jaccard weight).
        var score = _sut.Score("ALPHA BETA GAMMA", "GAMMA BETA ALPHA");
        score.Should().BeGreaterThanOrEqualTo(0.4);
    }

    [Fact]
    public void Score_SubstringMatch_StillGetsCredit()
    {
        // "ACME" is a substring of "ACMECORP" — JW will be moderate, Jaccard is 0 (no shared tokens).
        var score = _sut.Score("ACME", "ACMECORP");
        score.Should().BeGreaterThan(0.3);
    }
}
