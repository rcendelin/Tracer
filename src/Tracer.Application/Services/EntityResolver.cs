using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Tracer.Application.DTOs;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Resolves entity identity for CKB deduplication.
/// Matching pipeline: RegistrationId+Country → NormalizedKey → null (new profile).
/// </summary>
public sealed partial class EntityResolver : IEntityResolver
{
    private readonly ICompanyProfileRepository _profileRepository;

    public EntityResolver(ICompanyProfileRepository profileRepository)
    {
        _profileRepository = profileRepository;
    }

    public async Task<CompanyProfile?> ResolveAsync(TraceRequestDto input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        // 1. Exact match: RegistrationId + Country
        if (!string.IsNullOrWhiteSpace(input.RegistrationId) &&
            !string.IsNullOrWhiteSpace(input.Country))
        {
            var profile = await _profileRepository.FindByRegistrationIdAsync(
                input.RegistrationId, input.Country, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
                return profile;

            // Also try by normalized key
            var regKey = $"{input.Country}:{input.RegistrationId}";
            profile = await _profileRepository.FindByKeyAsync(regKey, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
                return profile;
        }

        // 2. Name-based match via NormalizedKey
        if (!string.IsNullOrWhiteSpace(input.CompanyName))
        {
            var nameKey = GenerateNormalizedKey(input.CompanyName, input.Country, input.RegistrationId);
            var profile = await _profileRepository.FindByKeyAsync(nameKey, cancellationToken).ConfigureAwait(false);
            if (profile is not null)
                return profile;
        }

        // 3. No match — new company
        return null;
    }

    public string GenerateNormalizedKey(string? name, string? country, string? registrationId)
    {
        // Prefer RegistrationId-based key
        if (!string.IsNullOrWhiteSpace(registrationId) && !string.IsNullOrWhiteSpace(country))
            return $"{country.Trim().ToUpperInvariant()}:{registrationId.Trim()}";

        // Fall back to name-based key
        if (!string.IsNullOrWhiteSpace(name))
        {
            var normalized = NormalizeName(name);
            var hash = ComputeHash(normalized);
            var countryPrefix = !string.IsNullOrWhiteSpace(country)
                ? country.Trim().ToUpperInvariant()
                : "XX";
            return $"NAME:{countryPrefix}:{hash}";
        }

        return $"UNKNOWN:{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Normalizes a company name for deduplication:
    /// 1. Lowercase
    /// 2. Remove diacritics
    /// 3. Remove legal form suffixes
    /// 4. Remove punctuation
    /// 5. Sort tokens alphabetically
    /// 6. SHA256 hash
    /// </summary>
    internal static string NormalizeName(string name)
    {
        var result = name.Trim().ToUpperInvariant();

        // Remove diacritics
        result = RemoveDiacritics(result);

        // Remove legal forms
        result = LegalFormPattern().Replace(result, " ");

        // Remove punctuation and extra whitespace
        result = PunctuationPattern().Replace(result, " ");
        result = WhitespacePattern().Replace(result, " ").Trim();

        // Sort tokens for order-independent matching
        var tokens = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Array.Sort(tokens, StringComparer.Ordinal);

        return string.Join(" ", tokens);
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..16]; // First 16 hex chars (64 bits)
    }

    // Common legal form patterns across countries
    [GeneratedRegex(
        @"\b(s\.?r\.?o\.?|a\.?s\.?|spol\.?\s*s\s*r\.?\s*o\.?|v\.?o\.?s\.?" +
        @"|gmbh|ag|kg|ohg|ug|e\.?v\.?" +
        @"|ltd\.?|llc|llp|plc|inc\.?|corp\.?|co\.?" +
        @"|sa|sarl|sas|srl|spa|snc" +
        @"|pty|nv|bv|cv" +
        @"|ab|aps|as|oy|oyj" +
        @"|kft|zrt|nyrt|bt)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LegalFormPattern();

    [GeneratedRegex(@"[^\w\s]", RegexOptions.Compiled)]
    private static partial Regex PunctuationPattern();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespacePattern();
}
