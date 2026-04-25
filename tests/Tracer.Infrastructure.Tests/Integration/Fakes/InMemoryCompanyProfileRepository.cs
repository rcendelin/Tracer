using System.Collections.Concurrent;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// In-memory stand-in for <see cref="ICompanyProfileRepository"/> used by E2E tests.
/// Stores profiles in a <see cref="ConcurrentDictionary{TKey, TValue}"/> keyed by Id so that
/// <see cref="InMemoryUnitOfWork"/> can iterate entities and dispatch their domain events
/// on <c>SaveChangesAsync</c>.
/// </summary>
internal sealed class InMemoryCompanyProfileRepository : ICompanyProfileRepository
{
    private readonly ConcurrentDictionary<Guid, CompanyProfile> _byId = new();

    /// <summary>Direct access for test seeding + assertions. Not part of the repository contract.</summary>
    public IReadOnlyCollection<CompanyProfile> All => _byId.Values.ToList();

    public Task<CompanyProfile?> FindByKeyAsync(string normalizedKey, CancellationToken cancellationToken = default)
    {
        var match = _byId.Values.FirstOrDefault(p => p.NormalizedKey == normalizedKey);
        return Task.FromResult(match);
    }

    public Task<CompanyProfile?> FindByRegistrationIdAsync(
        string registrationId, string country, CancellationToken cancellationToken = default)
    {
        var match = _byId.Values.FirstOrDefault(p =>
            p.RegistrationId == registrationId && p.Country == country);
        return Task.FromResult(match);
    }

    public Task UpsertAsync(CompanyProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _byId[profile.Id] = profile;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<CompanyProfile>> GetRevalidationQueueAsync(
        int maxCount, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<CompanyProfile> result = _byId.Values
            .Where(p => !p.IsArchived && p.NeedsRevalidation())
            .OrderByDescending(p => p.TraceCount)
            .Take(maxCount)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<CompanyProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var p) ? p : null);

    public Task<IReadOnlyCollection<CompanyProfile>> ListAsync(
        int page, int pageSize,
        string? search = null,
        string? country = null,
        double? minConfidence = null,
        double? maxConfidence = null,
        DateTimeOffset? validatedBefore = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<CompanyProfile> result = _byId.Values
            .Where(p => includeArchived || !p.IsArchived)
            .Where(p => country is null || p.Country == country)
            .Where(p => search is null || (p.RegistrationId?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(p => p.NormalizedKey)
            .Skip(page * pageSize)
            .Take(pageSize)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.ContainsKey(id));

    public Task<IReadOnlyCollection<CompanyProfile>> ListByCountryAsync(
        string country, int maxCount, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<CompanyProfile> result = _byId.Values
            .Where(p => !p.IsArchived && string.Equals(p.Country, country, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.TraceCount)
            .Take(maxCount)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyCollection<CompanyProfile>> ListByCountryAsync(
        string country, int maxCount, int minTraceCount, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<CompanyProfile> result = _byId.Values
            .Where(p => !p.IsArchived
                        && string.Equals(p.Country, country, StringComparison.OrdinalIgnoreCase)
                        && p.TraceCount >= minTraceCount)
            .OrderByDescending(p => p.TraceCount)
            .Take(maxCount)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> CountAsync(
        string? search = null,
        string? country = null,
        double? minConfidence = null,
        double? maxConfidence = null,
        DateTimeOffset? validatedBefore = null,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var count = _byId.Values
            .Count(p => (includeArchived || !p.IsArchived)
                        && (country is null || p.Country == country));
        return Task.FromResult(count);
    }
}
