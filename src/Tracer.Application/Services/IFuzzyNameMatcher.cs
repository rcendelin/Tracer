namespace Tracer.Application.Services;

/// <summary>
/// Computes a similarity score between two normalized company names.
/// Combined metric: <c>0.6 × Jaro-Winkler + 0.4 × Token Jaccard</c>.
/// <para>
/// Inputs are expected to be pre-normalized by <see cref="ICompanyNameNormalizer"/>
/// (legal forms removed, diacritics stripped, tokens sorted). The matcher itself
/// does not normalize — callers must apply normalization upstream.
/// </para>
/// </summary>
public interface IFuzzyNameMatcher
{
    /// <summary>
    /// Computes the combined similarity score between two normalized names.
    /// </summary>
    /// <param name="normalizedName1">First normalized name (non-empty).</param>
    /// <param name="normalizedName2">Second normalized name (non-empty).</param>
    /// <returns>
    /// Combined score in <c>[0.0, 1.0]</c>. Symmetric: <c>Score(a, b) == Score(b, a)</c>.
    /// Identical strings return <c>1.0</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Either argument is <see langword="null"/> or whitespace.
    /// </exception>
    double Score(string normalizedName1, string normalizedName2);
}
