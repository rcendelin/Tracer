using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Coordinates persistence of enrichment results to the Company Knowledge Base (CKB).
/// Handles profile upsert, change event creation, and source result recording.
/// </summary>
public interface ICkbPersistenceService
{
    /// <summary>
    /// Persists enrichment results for a trace request:
    /// 1. Upserts the company profile with merged fields
    /// 2. Records source results for audit
    /// 3. Collects and persists change events
    /// 4. Updates CKB metadata (LastEnrichedAt, TraceCount, OverallConfidence)
    /// </summary>
    /// <param name="profile">The company profile to persist (new or existing).</param>
    /// <param name="sourceResults">Provider execution results for audit trail.</param>
    /// <param name="mergeResult">The merged golden record fields.</param>
    /// <param name="traceRequestId">The originating trace request ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PersistEnrichmentAsync(
        CompanyProfile profile,
        IReadOnlyCollection<(string ProviderId, ProviderResult Result)> sourceResults,
        MergeResult mergeResult,
        Guid traceRequestId,
        CancellationToken cancellationToken);
}
