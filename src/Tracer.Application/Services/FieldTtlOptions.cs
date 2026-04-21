namespace Tracer.Application.Services;

/// <summary>
/// Configuration for the field TTL policy. Bound from the
/// <c>Revalidation:FieldTtl</c> section of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// The dictionary shape mirrors the existing <c>appsettings.json</c>
/// layout (flat <c>FieldName → TimeSpan</c> map). Missing fields fall back
/// to the platform defaults in <see cref="Tracer.Domain.ValueObjects.FieldTtl.For"/>.
/// <para>
/// Keys are matched against <see cref="Tracer.Domain.Enums.FieldName"/>
/// members case-insensitively. Unknown keys or non-positive durations
/// are rejected at startup by <c>ValidateOnStart()</c> — misconfiguration
/// fails fast rather than silently slipping through at first resolve.
/// </para>
/// </remarks>
public sealed class FieldTtlOptions
{
    /// <summary>Configuration section name (<c>Revalidation:FieldTtl</c>).</summary>
    public const string SectionName = "Revalidation:FieldTtl";

    /// <summary>
    /// Per-field TTL overrides. Keys correspond to <c>FieldName</c> members.
    /// Absent entries fall back to <see cref="Tracer.Domain.ValueObjects.FieldTtl.For"/>.
    /// </summary>
    public IDictionary<string, TimeSpan> Overrides { get; init; }
        = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);
}
