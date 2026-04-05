using Tracer.Domain.Common;
using Tracer.Domain.Enums;

namespace Tracer.Domain.Entities;

/// <summary>
/// Records the outcome of a single enrichment provider execution for a given trace request.
/// Provides per-provider audit trail and analytics data.
/// </summary>
public sealed class SourceResult : BaseEntity
{
    // EF Core parameterless constructor
    private SourceResult() { }

    public SourceResult(
        Guid traceRequestId,
        string providerId,
        SourceStatus status,
        int fieldsEnriched,
        long durationMs,
        string? errorMessage,
        string? rawResponseJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId, nameof(providerId));

        if (fieldsEnriched < 0)
            throw new ArgumentOutOfRangeException(nameof(fieldsEnriched), fieldsEnriched,
                "Fields enriched count cannot be negative.");

        if (durationMs < 0)
            throw new ArgumentOutOfRangeException(nameof(durationMs), durationMs,
                "Duration cannot be negative.");

        TraceRequestId = traceRequestId;
        ProviderId = providerId;
        Status = status;
        FieldsEnriched = fieldsEnriched;
        DurationMs = durationMs;
        ErrorMessage = errorMessage is { Length: > MaxErrorMessageLength }
            ? errorMessage[..MaxErrorMessageLength]
            : errorMessage;
        RawResponseJson = rawResponseJson is { Length: > MaxRawResponseLength }
            ? rawResponseJson[..MaxRawResponseLength]
            : rawResponseJson;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid TraceRequestId { get; private set; }

    /// <summary>
    /// Gets the provider identifier, e.g. <c>"ares"</c>, <c>"gleif"</c>.
    /// </summary>
    public string ProviderId { get; private set; } = null!;

    public SourceStatus Status { get; private set; }
    public int FieldsEnriched { get; private set; }
    public long DurationMs { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Gets the raw JSON response from the provider for debugging and audit purposes.
    /// May be <see langword="null"/> if the provider did not return a response body.
    /// </summary>
    public string? RawResponseJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private const int MaxErrorMessageLength = 2000;
    private const int MaxRawResponseLength = 50_000;
}
