using System.Collections.Immutable;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Interfaces;

/// <summary>
/// Immutable context passed to each <see cref="IEnrichmentProvider"/> during the waterfall pipeline.
/// Contains the original request input, any existing CKB profile, and fields accumulated
/// from higher-priority providers so that lower-priority providers can avoid redundant work.
/// </summary>
public sealed record TraceContext
{
    /// <summary>Gets the original trace request that initiated this enrichment.</summary>
    public required TraceRequest Request { get; init; }

    /// <summary>
    /// Gets the existing company profile from CKB, or <see langword="null"/> if this is a new company.
    /// Providers can use this to compare current vs. new values.
    /// </summary>
    public CompanyProfile? ExistingProfile { get; init; }

    /// <summary>
    /// Gets the set of fields already enriched by higher-priority providers in the current waterfall run.
    /// Lower-priority providers can skip fields that are already present with sufficient confidence.
    /// </summary>
    public IReadOnlySet<FieldName> AccumulatedFields { get; init; } = ImmutableHashSet<FieldName>.Empty;

    /// <summary>
    /// Gets the requested enrichment depth, controlling which provider tiers to invoke.
    /// </summary>
    public TraceDepth Depth => Request.Depth;

    /// <summary>
    /// Gets the country hint for provider selection (e.g. ARES for CZ, Companies House for GB).
    /// </summary>
    public string? Country => Request.Country;
}
