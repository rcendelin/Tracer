using Tracer.Domain.Entities;

namespace Tracer.Application.Services;

/// <summary>
/// Orchestrates the waterfall enrichment pipeline for a trace request.
/// Runs enrichment providers in priority order and assembles the golden record.
/// Implemented in <c>Tracer.Infrastructure</c>.
/// </summary>
public interface IWaterfallOrchestrator
{
    /// <summary>
    /// Executes the enrichment pipeline for the given trace request.
    /// </summary>
    /// <param name="request">The trace request (must be in InProgress state).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The enriched or matched company profile.</returns>
    Task<CompanyProfile> ExecuteAsync(TraceRequest request, CancellationToken cancellationToken);
}
