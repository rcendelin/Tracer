using Microsoft.Extensions.Options;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Services;

/// <summary>
/// Default <see cref="IFieldTtlPolicy"/> implementation. Precomputes a
/// <see cref="FieldName"/>-keyed lookup from configured overrides, falling
/// back to <see cref="FieldTtl.For"/> for any field without an override.
/// </summary>
/// <remarks>
/// Constructor validates inputs defensively even though <c>ValidateOnStart()</c>
/// in <c>Program.cs</c> normally catches misconfiguration — unit tests and
/// callers constructing this type directly must not be able to obtain an
/// instance with invalid state.
/// </remarks>
internal sealed class FieldTtlPolicy : IFieldTtlPolicy
{
    private readonly IReadOnlyDictionary<FieldName, TimeSpan> _effectiveTtls;

    public FieldTtlPolicy(IOptions<FieldTtlOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var overrides = options.Value.Overrides ?? new Dictionary<string, TimeSpan>();
        var map = new Dictionary<FieldName, TimeSpan>();

        foreach (var (key, ttl) in overrides)
        {
            if (!Enum.TryParse<FieldName>(key, ignoreCase: true, out var field))
                throw new ArgumentException(
                    $"Revalidation:FieldTtl contains unknown field name '{key}'.",
                    nameof(options));

            if (ttl <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    ttl,
                    $"Revalidation:FieldTtl['{key}'] must be a strictly positive TimeSpan.");

            map[field] = ttl;
        }

        _effectiveTtls = map;
    }

    public TimeSpan GetTtl(FieldName field) =>
        _effectiveTtls.TryGetValue(field, out var ttl) ? ttl : FieldTtl.For(field).Ttl;

    public IReadOnlyList<FieldName> GetExpiredFields(CompanyProfile profile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var expired = new List<FieldName>();
        foreach (var (field, enrichedAt) in EnumerateEnrichedFields(profile))
        {
            if (now - enrichedAt > GetTtl(field))
                expired.Add(field);
        }
        return expired;
    }

    public DateTimeOffset? GetNextExpirationDate(CompanyProfile profile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);

        DateTimeOffset? earliest = null;
        foreach (var (field, enrichedAt) in EnumerateEnrichedFields(profile))
        {
            var expiration = enrichedAt + GetTtl(field);
            if (earliest is null || expiration < earliest)
                earliest = expiration;
        }
        return earliest;
    }

    public bool IsRevalidationDue(CompanyProfile profile, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var (field, enrichedAt) in EnumerateEnrichedFields(profile))
        {
            if (now - enrichedAt > GetTtl(field))
                return true;
        }
        return false;
    }

    private static IEnumerable<(FieldName Field, DateTimeOffset EnrichedAt)> EnumerateEnrichedFields(
        CompanyProfile profile)
    {
        if (profile.LegalName is not null)         yield return (FieldName.LegalName, profile.LegalName.EnrichedAt);
        if (profile.TradeName is not null)         yield return (FieldName.TradeName, profile.TradeName.EnrichedAt);
        if (profile.TaxId is not null)             yield return (FieldName.TaxId, profile.TaxId.EnrichedAt);
        if (profile.LegalForm is not null)         yield return (FieldName.LegalForm, profile.LegalForm.EnrichedAt);
        if (profile.RegisteredAddress is not null) yield return (FieldName.RegisteredAddress, profile.RegisteredAddress.EnrichedAt);
        if (profile.OperatingAddress is not null)  yield return (FieldName.OperatingAddress, profile.OperatingAddress.EnrichedAt);
        if (profile.Phone is not null)             yield return (FieldName.Phone, profile.Phone.EnrichedAt);
        if (profile.Email is not null)             yield return (FieldName.Email, profile.Email.EnrichedAt);
        if (profile.Website is not null)           yield return (FieldName.Website, profile.Website.EnrichedAt);
        if (profile.Industry is not null)          yield return (FieldName.Industry, profile.Industry.EnrichedAt);
        if (profile.EmployeeRange is not null)     yield return (FieldName.EmployeeRange, profile.EmployeeRange.EnrichedAt);
        if (profile.EntityStatus is not null)      yield return (FieldName.EntityStatus, profile.EntityStatus.EnrichedAt);
        if (profile.ParentCompany is not null)     yield return (FieldName.ParentCompany, profile.ParentCompany.EnrichedAt);
        if (profile.Location is not null)          yield return (FieldName.Location, profile.Location.EnrichedAt);
        // RegistrationId is stored on CompanyProfile as a plain string identifier,
        // not a TracedField, so it has no EnrichedAt and is not subject to TTL expiry.
        // Officers is GDPR-gated and not stored as a TracedField property (see B-69).
    }
}
