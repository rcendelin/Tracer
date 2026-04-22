namespace Tracer.Api.Middleware;

/// <summary>
/// Typed configuration for API key authentication. Bound from the
/// <c>Auth</c> section. Supports both the legacy flat-string form
/// (<c>Auth:ApiKeys:0 = "the-key"</c>) and the rotation-friendly
/// structured form (<c>Auth:ApiKeys:0:Key = "the-key"</c>).
/// </summary>
internal sealed class ApiKeyOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Minimum length enforced by <c>ValidateOnStart</c>. Prevents typos like
    /// a single-character placeholder sneaking into production configuration.
    /// 16 chars × 6 bits ≈ 96 bits of entropy when the key is random.
    /// </summary>
    public const int MinimumKeyLength = 16;

    /// <summary>
    /// Parsed API key entries. Populated by <see cref="ApiKeyOptionsBinder"/>
    /// which understands both string and object forms.
    /// </summary>
    public IReadOnlyList<ApiKeyEntry> ApiKeys { get; init; } = [];
}

/// <summary>
/// A single API key with optional rotation metadata.
/// </summary>
internal sealed record ApiKeyEntry
{
    /// <summary>The secret. Stored as-is; callers compare via ordinal equality.</summary>
    public required string Key { get; init; }

    /// <summary>Human-readable label for audit logs (e.g. "ci", "internal-ops").</summary>
    public string? Label { get; init; }

    /// <summary>
    /// Optional expiration timestamp. When set, the key is ignored after this
    /// instant even if present in configuration — supports overlapping key
    /// rotation without redeploy.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    public bool IsActive(DateTimeOffset now) =>
        ExpiresAt is null || ExpiresAt.Value > now;
}
