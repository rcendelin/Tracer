using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracer.Domain.Entities;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Services;

/// <summary>
/// Composite re-validation runner (B-66): dispatches to the lightweight or
/// deep runner based on the count of expired fields. Registered as the
/// production <see cref="IRevalidationRunner"/> so the scheduler binds to a
/// single, stable abstraction.
/// </summary>
/// <remarks>
/// <para>
/// **Threshold semantics.** With default options:
/// <list type="bullet">
///   <item>0–2 expired fields → lightweight (timestamp-only refresh).</item>
///   <item>≥ 3 expired fields → deep (full waterfall re-enrichment).</item>
/// </list>
/// The deep threshold (<c>Revalidation:Deep:Threshold</c>) is consulted by the
/// deep runner itself; this composite uses only the lightweight bound. If
/// <c>Revalidation:Lightweight:Enabled = false</c>, every profile goes deep —
/// equivalent to pre-B-66 behaviour.
/// </para>
/// <para>
/// **Save boundary.** The composite never calls <c>SaveChangesAsync</c>; it
/// faithfully delegates the save semantics of whichever runner it picked.
/// Lightweight leaves persistence to the scheduler; deep saves twice
/// internally (see <see cref="DeepRevalidationRunner"/>).
/// </para>
/// </remarks>
internal sealed class CompositeRevalidationRunner : IRevalidationRunner
{
    private readonly LightweightRevalidationRunner _lightweight;
    private readonly DeepRevalidationRunner _deep;
    private readonly IFieldTtlPolicy _ttlPolicy;
    private readonly LightweightRevalidationOptions _options;
    private readonly ILogger<CompositeRevalidationRunner> _logger;

    public CompositeRevalidationRunner(
        LightweightRevalidationRunner lightweight,
        DeepRevalidationRunner deep,
        IFieldTtlPolicy ttlPolicy,
        IOptions<LightweightRevalidationOptions> options,
        ILogger<CompositeRevalidationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(lightweight);
        ArgumentNullException.ThrowIfNull(deep);
        ArgumentNullException.ThrowIfNull(ttlPolicy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _lightweight = lightweight;
        _deep = deep;
        _ttlPolicy = ttlPolicy;
        _options = options.Value;
        _logger = logger;
    }

    public Task<RevalidationOutcome> RunAsync(
        CompanyProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!_options.Enabled)
            return _deep.RunAsync(profile, cancellationToken);

        var expiredCount = _ttlPolicy.GetExpiredFields(profile, DateTimeOffset.UtcNow).Count;
        if (expiredCount > _options.Threshold)
        {
            _logger.LogDebug(
                "Composite runner → deep for profile {ProfileId} ({ExpiredCount} expired > {Threshold}).",
                profile.Id, expiredCount, _options.Threshold);
            return _deep.RunAsync(profile, cancellationToken);
        }

        _logger.LogDebug(
            "Composite runner → lightweight for profile {ProfileId} ({ExpiredCount} expired ≤ {Threshold}).",
            profile.Id, expiredCount, _options.Threshold);
        return _lightweight.RunAsync(profile, cancellationToken);
    }
}
