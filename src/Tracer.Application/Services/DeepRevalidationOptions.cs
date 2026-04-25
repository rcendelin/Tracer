namespace Tracer.Application.Services;

/// <summary>
/// Configuration for <see cref="DeepRevalidationRunner"/>.
/// Bound from the <c>Revalidation:Deep</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class DeepRevalidationOptions
{
    public const string SectionName = "Revalidation:Deep";

    /// <summary>
    /// Minimum number of expired fields on a profile that triggers a full
    /// waterfall re-enrichment. Below this threshold the runner returns
    /// <see cref="RevalidationOutcome.Deferred"/> (expected to be picked up
    /// by the lightweight mode once B-66 is merged). Default: 3.
    /// </summary>
    public int Threshold { get; init; } = 3;
}
