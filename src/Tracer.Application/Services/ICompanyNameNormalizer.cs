namespace Tracer.Application.Services;

/// <summary>
/// Normalizes company names for deduplication and matching in the CKB.
/// Produces deterministic, order-independent, language-agnostic representations
/// of company names by removing legal forms, diacritics, stopwords, and applying
/// transliteration and abbreviation expansion.
/// </summary>
public interface ICompanyNameNormalizer
{
    /// <summary>
    /// Normalizes a company name for deduplication.
    /// </summary>
    /// <param name="companyName">The company name to normalize.</param>
    /// <returns>
    /// A deterministic normalized representation suitable for hashing and comparison.
    /// Tokens are sorted alphabetically for order-independent matching.
    /// </returns>
    string Normalize(string companyName);
}
