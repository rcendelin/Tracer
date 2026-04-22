namespace Tracer.Application.Services;

/// <summary>
/// Defines a daily off-peak time window in UTC, used by the re-validation
/// scheduler to throttle automatic runs to low-traffic hours.
/// </summary>
/// <remarks>
/// Semantics: an hour <c>h</c> is "inside" the window when
/// <c>StartHourUtc &lt;= h &lt; EndHourUtc</c> (same-day window) or,
/// for wrap-around windows (e.g. 22–6), when
/// <c>h &gt;= StartHourUtc OR h &lt; EndHourUtc</c>.
/// If <see cref="StartHourUtc"/> equals <see cref="EndHourUtc"/>, the
/// window is empty (i.e. always outside).
/// </remarks>
public sealed class OffPeakWindow
{
    /// <summary>
    /// When <c>false</c> (default), the off-peak gate is disabled and the
    /// scheduler runs every tick. When <c>true</c>, automatic runs only
    /// execute inside the window. Manual queue items are unaffected.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Inclusive start hour (0–23, UTC). Default: 22.</summary>
    public int StartHourUtc { get; init; } = 22;

    /// <summary>Exclusive end hour (0–23, UTC). Default: 6.</summary>
    public int EndHourUtc { get; init; } = 6;

    /// <summary>
    /// Returns <c>true</c> if <paramref name="now"/> falls inside the
    /// configured off-peak window in UTC. When <see cref="Enabled"/> is
    /// <c>false</c>, this method always returns <c>true</c> (no gating).
    /// </summary>
    public bool IsWithin(DateTimeOffset now)
    {
        if (!Enabled)
            return true;

        if (StartHourUtc == EndHourUtc)
            return false;

        var hour = now.ToUniversalTime().Hour;

        return StartHourUtc < EndHourUtc
            ? hour >= StartHourUtc && hour < EndHourUtc
            : hour >= StartHourUtc || hour < EndHourUtc;
    }
}
