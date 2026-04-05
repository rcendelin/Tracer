namespace Tracer.Domain.Interfaces;

/// <summary>
/// Abstraction for a single data source in the enrichment waterfall pipeline.
/// Each implementation connects to one external data source (registry API, scraper, AI extractor).
/// </summary>
/// <remarks>
/// Providers are executed in <see cref="Priority"/> order by the waterfall orchestrator.
/// Lower priority values run first. Within the same tier, providers may run in parallel.
/// </remarks>
public interface IEnrichmentProvider
{
    /// <summary>
    /// Gets the unique identifier of this provider, e.g. <c>"ares"</c>, <c>"gleif"</c>, <c>"google-maps"</c>.
    /// Used for audit trails, source attribution in <c>TracedField</c>, and configuration keys.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Gets the execution priority. Lower values run earlier in the waterfall.
    /// See the provider priority tiers in CLAUDE.md (10–20 = Registry API, 50 = Geo, 150 = Scraping, 250 = AI).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets the inherent quality/trustworthiness of this source on a [0.0, 1.0] scale.
    /// Used by the confidence scorer to weight field values from this provider.
    /// Official registries (ARES, Companies House) should be close to 1.0;
    /// AI extraction should be lower (e.g. 0.6).
    /// </summary>
    double SourceQuality { get; }

    /// <summary>
    /// Determines whether this provider can handle the given trace context.
    /// Typically checks country, available input fields, and requested depth.
    /// </summary>
    /// <param name="context">The enrichment context containing request input and accumulated data.</param>
    /// <returns><see langword="true"/> if this provider should be invoked for the given context.</returns>
    bool CanHandle(TraceContext context);

    /// <summary>
    /// Executes the enrichment against this provider's external data source.
    /// </summary>
    /// <param name="context">The enrichment context containing request input and accumulated data.</param>
    /// <param name="cancellationToken">Cancellation token for timeout and pipeline abort.</param>
    /// <returns>A <see cref="ProviderResult"/> with enriched fields or error information.</returns>
    Task<ProviderResult> EnrichAsync(TraceContext context, CancellationToken cancellationToken);
}
