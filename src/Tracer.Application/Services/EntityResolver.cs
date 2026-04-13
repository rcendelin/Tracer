using System.Security.Cryptography;
using System.Text;
using Tracer.Application.DTOs;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Resolves entity identity for CKB deduplication.
/// Matching pipeline: RegistrationId+Country → NormalizedKey → null (new profile).
/// </summary>
public sealed class EntityResolver : IEntityResolver
{
    private readonly ICompanyProfileRepository _profileRepository;
    private readonly ICompanyNameNormalizer _normalizer;

    public EntityResolver(ICompanyProfileRepository profileRepository, ICompanyNameNormalizer normalizer)
    {
        _profileRepository = profileRepository;
        _normalizer = normalizer;
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
            var normalized = _normalizer.Normalize(name);
            var hash = ComputeHash(normalized);
            var countryPrefix = !string.IsNullOrWhiteSpace(country)
                ? country.Trim().ToUpperInvariant()
                : "XX";
            return $"NAME:{countryPrefix}:{hash}";
        }

        return $"UNKNOWN:{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Normalizes a company name for deduplication.
    /// Delegates to <see cref="ICompanyNameNormalizer"/> for the enhanced pipeline.
    /// Kept as internal static convenience for backward-compatible test access via a default normalizer.
    /// </summary>
    internal static string NormalizeName(string name) =>
        new CompanyNameNormalizer().Normalize(name);

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes)[..16]; // First 16 hex chars (64 bits)
    }
}
