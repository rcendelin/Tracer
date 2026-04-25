namespace Tracer.Application.Services;

/// <summary>
/// Configuration for the B-66 lightweight re-validation pathway. Bound from
/// the <c>Revalidation:Lightweight</c> section.
/// </summary>
/// <remarks>
/// The lightweight pathway only refreshes <c>EnrichedAt</c> on fields whose
/// value has not actually changed — it does not call out to any provider.
/// The composite runner (<see cref="CompositeRevalidationRunner"/>) chooses
/// between lightweight and deep based on the count of expired fields.
/// </remarks>
public sealed class LightweightRevalidationOptions
{
    public const string SectionName = "Revalidation:Lightweight";

    /// <summary>
    /// Master toggle. When <see langword="false"/> the composite runner always
    /// delegates to the deep runner — equivalent to the pre-B-66 behaviour.
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Inclusive upper bound on the number of expired fields that still
    /// qualify for the lightweight pathway. Profiles with strictly more
    /// expired fields go through the deep waterfall. Default: 2.
    /// </summary>
    /// <remarks>
    /// Should stay strictly below <c>Revalidation:Deep:Threshold</c> (default 3)
    /// so the two thresholds do not overlap. Validated at startup.
    /// </remarks>
    public int Threshold { get; set; } = 2;
}
