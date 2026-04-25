using Tracer.Domain.Enums;

namespace Tracer.Domain.Interfaces;

/// <summary>
/// The output of a single <see cref="IEnrichmentProvider"/> execution.
/// Contains enriched fields, raw response data, and timing information.
/// Use the static factory methods to create instances.
/// </summary>
public sealed record ProviderResult
{
    private ProviderResult() { }

    /// <summary>Gets whether the provider found a matching company.</summary>
    public bool Found { get; private init; }

    /// <summary>
    /// Gets the enriched fields as a dictionary of field name to raw value.
    /// Values are untyped objects that the orchestrator will map to <c>TracedField&lt;T&gt;</c>
    /// using the provider's <see cref="IEnrichmentProvider.SourceQuality"/>.
    /// </summary>
    public IReadOnlyDictionary<FieldName, object?> Fields { get; private init; }
        = new Dictionary<FieldName, object?>();

    /// <summary>
    /// Gets the raw JSON response from the provider, stored for audit/debugging.
    /// May be <see langword="null"/> for providers that don't return JSON.
    /// </summary>
    public string? RawResponseJson { get; private init; }

    /// <summary>Gets the provider execution duration.</summary>
    public TimeSpan Duration { get; private init; }

    /// <summary>Gets the error message if the provider failed.</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>Gets the outcome status of the provider execution.</summary>
    public SourceStatus Status { get; private init; }

    // ── Factory methods ─────────────────────────────────────────────

    /// <summary>
    /// Creates a successful result with enriched fields.
    /// </summary>
    public static ProviderResult Success(
        IReadOnlyDictionary<FieldName, object?> fields,
        TimeSpan duration,
        string? rawResponseJson = null) =>
        new()
        {
            Found = true,
            Fields = fields,
            Duration = duration,
            RawResponseJson = rawResponseJson,
            Status = SourceStatus.Success,
        };

    /// <summary>
    /// Creates a result indicating the provider found no matching company.
    /// </summary>
    public static ProviderResult NotFound(TimeSpan duration) =>
        new()
        {
            Found = false,
            Duration = duration,
            Status = SourceStatus.NotFound,
        };

    /// <summary>
    /// Creates a result indicating the provider encountered an error.
    /// </summary>
    public static ProviderResult Error(string errorMessage, TimeSpan duration) =>
        new()
        {
            Found = false,
            Duration = duration,
            ErrorMessage = errorMessage,
            Status = SourceStatus.Error,
        };

    /// <summary>
    /// Creates a result indicating the provider timed out.
    /// </summary>
    public static ProviderResult Timeout(TimeSpan duration) =>
        new()
        {
            Found = false,
            Duration = duration,
            Status = SourceStatus.Timeout,
        };

    /// <summary>
    /// Creates a result indicating the provider was skipped (CanHandle returned false).
    /// </summary>
    public static ProviderResult Skipped() =>
        new()
        {
            Found = false,
            Duration = TimeSpan.Zero,
            Status = SourceStatus.Skipped,
        };
}
