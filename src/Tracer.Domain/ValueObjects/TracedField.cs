namespace Tracer.Domain.ValueObjects;

/// <summary>
/// The fundamental data unit of the Tracer enrichment engine.
/// Every enriched field on a company profile carries its own provenance:
/// the raw value, a confidence score, the ID of the provider that produced
/// it, and the timestamp when it was produced.
/// </summary>
/// <typeparam name="T">The type of the enriched value.</typeparam>
public sealed record TracedField<T>
{
    /// <summary>Gets the enriched value.</summary>
    public required T Value { get; init; }

    /// <summary>
    /// Gets the confidence score for this field value.
    /// Range: 0.0 (no confidence) to 1.0 (absolute certainty).
    /// Use <see cref="Confidence.Create"/> or the explicit cast operator to construct the value.
    /// </summary>
    public required Confidence Confidence { get; init; }

    /// <summary>
    /// Gets the identifier of the provider that produced this value,
    /// e.g. <c>"ares"</c>, <c>"gleif"</c>, <c>"google-maps"</c>.
    /// Must be a non-empty, non-whitespace string.
    /// </summary>
    public required string Source
    {
        get => _source;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(Source));
            _source = value;
        }
    }

    private string _source = string.Empty;

    /// <summary>Gets the UTC timestamp when this value was enriched.</summary>
    public required DateTimeOffset EnrichedAt { get; init; }

    /// <summary>
    /// Returns <see langword="true"/> when the field value has exceeded its
    /// time-to-live and should be considered stale.
    /// </summary>
    /// <param name="ttl">The maximum age before this field is considered stale.</param>
    public bool IsExpired(TimeSpan ttl) => DateTimeOffset.UtcNow - EnrichedAt > ttl;
}
