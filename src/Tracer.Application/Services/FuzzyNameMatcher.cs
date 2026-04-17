namespace Tracer.Application.Services;

/// <summary>
/// Fuzzy name matcher combining Jaro-Winkler similarity (60%) and Token Jaccard
/// similarity (40%) into a single score.
/// <para>
/// Stateless and thread-safe — register as Singleton.
/// </para>
/// <para>
/// Design notes:
/// <list type="bullet">
///   <item>
///     Jaro-Winkler favours strings with a common prefix — good for company name typos
///     and short prefixes shared by related entities.
///   </item>
///   <item>
///     Token Jaccard treats each whitespace-separated token as a set element — good for
///     order-independent multi-word matching ("Banco Santander" vs "Santander Banco").
///   </item>
///   <item>
///     The 60/40 weighting biases toward character-level matching (JW) while still
///     crediting token set overlap.
///   </item>
/// </list>
/// </para>
/// </summary>
public sealed class FuzzyNameMatcher : IFuzzyNameMatcher
{
    // Weighting for the combined score.
    private const double JaroWinklerWeight = 0.6;
    private const double TokenJaccardWeight = 0.4;

    // Jaro-Winkler prefix scaling factor (standard value p = 0.1, max 4 chars).
    private const double PrefixScalingFactor = 0.1;
    private const int MaxPrefixLength = 4;

    /// <inheritdoc />
    public double Score(string normalizedName1, string normalizedName2)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName1, nameof(normalizedName1));
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedName2, nameof(normalizedName2));

        // Fast path: identical strings.
        if (string.Equals(normalizedName1, normalizedName2, StringComparison.Ordinal))
            return 1.0;

        var jw = JaroWinkler(normalizedName1, normalizedName2);
        var jaccard = TokenJaccard(normalizedName1, normalizedName2);

        var combined = (JaroWinklerWeight * jw) + (TokenJaccardWeight * jaccard);

        // Clamp to [0.0, 1.0] to guard against any floating-point drift.
        if (combined < 0.0) return 0.0;
        if (combined > 1.0) return 1.0;
        return combined;
    }

    // ── Jaro-Winkler similarity ──────────────────────────────────────────────

    /// <summary>
    /// Computes the Jaro-Winkler similarity between two strings.
    /// Returns <c>0.0</c> for fully distinct, <c>1.0</c> for identical.
    /// </summary>
    private static double JaroWinkler(string s1, string s2)
    {
        var jaro = Jaro(s1, s2);
        if (jaro <= 0.0)
            return 0.0;

        var prefix = CommonPrefixLength(s1, s2, MaxPrefixLength);
        return jaro + (prefix * PrefixScalingFactor * (1.0 - jaro));
    }

    /// <summary>
    /// Computes the base Jaro similarity.
    /// Matching window = <c>max(len1, len2) / 2 − 1</c>.
    /// </summary>
    private static double Jaro(string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;

        if (len1 == 0 && len2 == 0) return 1.0;
        if (len1 == 0 || len2 == 0) return 0.0;

        var matchDistance = Math.Max(len1, len2) / 2 - 1;
        if (matchDistance < 0) matchDistance = 0;

        var s1Matches = new bool[len1];
        var s2Matches = new bool[len2];
        var matches = 0;

        // Find matching characters within the window.
        for (var i = 0; i < len1; i++)
        {
            var start = Math.Max(0, i - matchDistance);
            var end = Math.Min(i + matchDistance + 1, len2);

            for (var j = start; j < end; j++)
            {
                if (s2Matches[j]) continue;
                if (s1[i] != s2[j]) continue;

                s1Matches[i] = true;
                s2Matches[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0;

        // Count transpositions (matched chars out of order).
        var transpositions = 0;
        var k = 0;
        for (var i = 0; i < len1; i++)
        {
            if (!s1Matches[i]) continue;
            while (!s2Matches[k]) k++;
            if (s1[i] != s2[k]) transpositions++;
            k++;
        }

        var m = (double)matches;
        var t = transpositions / 2.0;

        return (m / len1 + m / len2 + (m - t) / m) / 3.0;
    }

    /// <summary>
    /// Returns the length of the common prefix between two strings, capped at <paramref name="maxLength"/>.
    /// </summary>
    private static int CommonPrefixLength(string s1, string s2, int maxLength)
    {
        var limit = Math.Min(maxLength, Math.Min(s1.Length, s2.Length));
        for (var i = 0; i < limit; i++)
        {
            if (s1[i] != s2[i]) return i;
        }
        return limit;
    }

    // ── Token Jaccard similarity ─────────────────────────────────────────────

    /// <summary>
    /// Computes Jaccard similarity <c>|A ∩ B| / |A ∪ B|</c> over space-separated tokens.
    /// </summary>
    private static double TokenJaccard(string s1, string s2)
    {
        var tokens1 = Tokenize(s1);
        var tokens2 = Tokenize(s2);

        if (tokens1.Count == 0 && tokens2.Count == 0) return 1.0;
        if (tokens1.Count == 0 || tokens2.Count == 0) return 0.0;

        var intersection = new HashSet<string>(tokens1, StringComparer.Ordinal);
        intersection.IntersectWith(tokens2);

        var union = new HashSet<string>(tokens1, StringComparer.Ordinal);
        union.UnionWith(tokens2);

        return (double)intersection.Count / union.Count;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new HashSet<string>(parts, StringComparer.Ordinal);
    }
}
