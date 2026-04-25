using System.Runtime.CompilerServices;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services.Export;

/// <summary>
/// Shared in-memory fakes for exporter tests — we want to exercise the full
/// async-enumerable streaming path without standing up EF Core.
/// </summary>
internal static class ExporterTestFakes
{
    public static TracedField<string> StringField(string value, string source = "ares") => new()
    {
        Value = value,
        Confidence = Confidence.Create(0.9),
        Source = source,
        EnrichedAt = DateTimeOffset.UtcNow,
    };

    public static CompanyProfile CreateProfile(string registrationId, string country, string legalName)
    {
        var profile = new CompanyProfile($"{country}:{registrationId}", country, registrationId);
        profile.UpdateField(FieldName.LegalName, StringField(legalName), "ares");
        return profile;
    }

    public static ChangeEvent CreateChangeEvent(
        Guid profileId,
        FieldName field = FieldName.LegalName,
        ChangeSeverity severity = ChangeSeverity.Major,
        string? prev = "\"Old\"",
        string? next = "\"New\"")
    {
        return new ChangeEvent(
            profileId,
            field,
            ChangeType.Updated,
            severity,
            prev,
            next,
            "ares");
    }

    internal sealed class FakeCompanyProfileRepository : ICompanyProfileRepository
    {
        private readonly List<CompanyProfile> _profiles;

        public FakeCompanyProfileRepository(IEnumerable<CompanyProfile> profiles)
        {
            _profiles = profiles.ToList();
        }

        public int CapturedMaxRows { get; private set; }

        public Task<CompanyProfile?> FindByKeyAsync(string normalizedKey, CancellationToken cancellationToken) =>
            Task.FromResult(_profiles.FirstOrDefault(p => p.NormalizedKey == normalizedKey));

        public Task<CompanyProfile?> FindByRegistrationIdAsync(string registrationId, string country, CancellationToken cancellationToken) =>
            Task.FromResult(_profiles.FirstOrDefault(p => p.RegistrationId == registrationId && p.Country == country));

        public Task UpsertAsync(CompanyProfile profile, CancellationToken cancellationToken)
        {
            _profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<CompanyProfile>> GetRevalidationQueueAsync(int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CompanyProfile>>(_profiles.Take(maxCount).ToList());

        public Task<CompanyProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_profiles.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyCollection<CompanyProfile>> ListAsync(
            int page, int pageSize,
            string? search, string? country, double? minConfidence, double? maxConfidence,
            DateTimeOffset? validatedBefore, bool includeArchived,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CompanyProfile>>(_profiles);

        public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_profiles.Any(p => p.Id == id));

        public Task<IReadOnlyCollection<CompanyProfile>> ListByCountryAsync(string country, int maxCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CompanyProfile>>(
                _profiles.Where(p => p.Country == country).Take(maxCount).ToList());

        public Task<IReadOnlyCollection<CompanyProfile>> ListByCountryAsync(string country, int maxCount, int minTraceCount, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CompanyProfile>>(
                _profiles.Where(p => p.Country == country && p.TraceCount >= minTraceCount).Take(maxCount).ToList());

        public Task<int> CountAsync(
            string? search, string? country, double? minConfidence, double? maxConfidence,
            DateTimeOffset? validatedBefore, bool includeArchived,
            CancellationToken cancellationToken) =>
            Task.FromResult(_profiles.Count);

        public async IAsyncEnumerable<CompanyProfile> StreamAsync(
            int maxRows,
            string? search, string? country, double? minConfidence, double? maxConfidence,
            DateTimeOffset? validatedBefore, bool includeArchived,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CapturedMaxRows = maxRows;
            var filtered = _profiles.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(country))
                filtered = filtered.Where(p => p.Country == country);

            foreach (var profile in filtered.Take(maxRows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return profile;
                await Task.Yield();
            }
        }
    }

    internal sealed class FakeChangeEventRepository : IChangeEventRepository
    {
        private readonly List<ChangeEvent> _events;

        public FakeChangeEventRepository(IEnumerable<ChangeEvent> events)
        {
            _events = events.ToList();
        }

        public int CapturedMaxRows { get; private set; }

        public Task<ChangeEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_events.FirstOrDefault(e => e.Id == id));

        public Task AddAsync(ChangeEvent changeEvent, CancellationToken cancellationToken)
        {
            _events.Add(changeEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<ChangeEvent>> ListByProfileAsync(
            Guid companyProfileId, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ChangeEvent>>(
                _events.Where(e => e.CompanyProfileId == companyProfileId).ToList());

        public Task<IReadOnlyCollection<ChangeEvent>> ListBySeverityAsync(
            ChangeSeverity minSeverity, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ChangeEvent>>(
                _events.Where(e => e.Severity >= minSeverity).ToList());

        public Task<int> CountByProfileAsync(Guid companyProfileId, CancellationToken cancellationToken) =>
            Task.FromResult(_events.Count(e => e.CompanyProfileId == companyProfileId));

        public Task<int> CountBySeverityAsync(ChangeSeverity minSeverity, CancellationToken cancellationToken) =>
            Task.FromResult(_events.Count(e => e.Severity >= minSeverity));

        public Task<IReadOnlyCollection<ChangeEvent>> ListAsync(
            int page, int pageSize, ChangeSeverity? severity, Guid? profileId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<ChangeEvent>>(_events);

        public Task<int> CountAsync(ChangeSeverity? severity, Guid? profileId, CancellationToken cancellationToken) =>
            Task.FromResult(_events.Count);

        public async IAsyncEnumerable<ChangeEvent> StreamAsync(
            int maxRows,
            ChangeSeverity? severity, Guid? profileId,
            DateTimeOffset? from, DateTimeOffset? to,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            CapturedMaxRows = maxRows;
            var filtered = _events.AsEnumerable();
            if (severity.HasValue)
                filtered = filtered.Where(e => e.Severity == severity.Value);
            if (profileId.HasValue)
                filtered = filtered.Where(e => e.CompanyProfileId == profileId.Value);
            if (from.HasValue)
                filtered = filtered.Where(e => e.DetectedAt >= from.Value);
            if (to.HasValue)
                filtered = filtered.Where(e => e.DetectedAt < to.Value);

            foreach (var evt in filtered.Take(maxRows))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return evt;
                await Task.Yield();
            }
        }
    }
}
