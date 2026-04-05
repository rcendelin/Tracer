using Tracer.Domain.Common;
using Tracer.Domain.Enums;
using Tracer.Domain.Events;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Entities;

/// <summary>
/// Represents a single enrichment request submitted to the Tracer engine.
/// Tracks the lifecycle from submission through completion or failure
/// and raises a <see cref="TraceCompletedEvent"/> when enrichment finishes.
/// </summary>
public sealed class TraceRequest : BaseEntity, IAggregateRoot
{
    // EF Core parameterless constructor
    private TraceRequest() { }

    /// <summary>
    /// Creates a new trace request with the specified input fields.
    /// </summary>
    public TraceRequest(
        string? companyName,
        string? phone,
        string? email,
        string? website,
        string? address,
        string? city,
        string? country,
        string? registrationId,
        string? taxId,
        string? industryHint,
        TraceDepth depth,
        Uri? callbackUrl,
        string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source, nameof(source));

        if (callbackUrl is not null && callbackUrl.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Callback URL must use HTTPS.", nameof(callbackUrl));

        CompanyName = companyName?.Trim();
        Phone = phone?.Trim();
        Email = email?.Trim();
        Website = website?.Trim();
        Address = address?.Trim();
        City = city?.Trim();
        Country = country?.Trim();
        RegistrationId = registrationId?.Trim();
        TaxId = taxId?.Trim();
        IndustryHint = industryHint?.Trim();
        Depth = depth;
        CallbackUrl = callbackUrl;
        Source = source;

        if (!HasAnyIdentifyingField())
            throw new ArgumentException(
                "At least one identifying field (companyName, registrationId, taxId, phone, email, or website) must be provided.");
        Status = TraceStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    // ── Input fields ────────────────────────────────────────────────
    public string? CompanyName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public string? Address { get; private set; }
    public string? City { get; private set; }
    public string? Country { get; private set; }
    public string? RegistrationId { get; private set; }
    public string? TaxId { get; private set; }
    public string? IndustryHint { get; private set; }

    // ── Control fields ──────────────────────────────────────────────
    public TraceDepth Depth { get; private set; }
    public Uri? CallbackUrl { get; private set; }
    public TraceStatus Status { get; private set; }

    /// <summary>
    /// Gets the origin of this request.
    /// Expected values: <c>"rest-api"</c>, <c>"service-bus"</c>, <c>"ui"</c>, <c>"revalidation"</c>.
    /// </summary>
    public string Source { get; private set; } = null!;

    // ── Result fields ───────────────────────────────────────────────
    public Guid? CompanyProfileId { get; private set; }
    public Confidence? OverallConfidence { get; private set; }
    public string? FailureReason { get; private set; }

    // ── Timestamps ──────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>
    /// Gets the total processing duration in milliseconds.
    /// Computed when the request transitions to a terminal state.
    /// </summary>
    public long? DurationMs { get; private set; }

    // ── Behaviour ───────────────────────────────────────────────────

    /// <summary>
    /// Transitions the request to <see cref="TraceStatus.InProgress"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the request is not in <see cref="TraceStatus.Pending"/> state.
    /// </exception>
    public void MarkInProgress()
    {
        if (Status != TraceStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot mark trace request as in-progress. Current status: {Status}.");

        Status = TraceStatus.InProgress;
    }

    /// <summary>
    /// Transitions the request to <see cref="TraceStatus.Completed"/> or
    /// <see cref="TraceStatus.PartiallyCompleted"/> and raises a <see cref="TraceCompletedEvent"/>.
    /// </summary>
    /// <param name="profileId">The ID of the enriched company profile.</param>
    /// <param name="confidence">The overall confidence of the enrichment result.</param>
    /// <param name="isPartial">
    /// When <see langword="true"/>, sets status to <see cref="TraceStatus.PartiallyCompleted"/>
    /// instead of <see cref="TraceStatus.Completed"/>.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the request is not in <see cref="TraceStatus.InProgress"/> state.
    /// </exception>
    public void Complete(Guid profileId, Confidence confidence, bool isPartial = false)
    {
        if (Status != TraceStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot complete trace request. Current status: {Status}.");

        CompanyProfileId = profileId;
        OverallConfidence = confidence;
        Status = isPartial ? TraceStatus.PartiallyCompleted : TraceStatus.Completed;
        SetCompletionTimestamps();

        AddDomainEvent(new TraceCompletedEvent(Id, profileId, Status, confidence));
    }

    /// <summary>
    /// Transitions the request to <see cref="TraceStatus.Failed"/> and raises a <see cref="TraceCompletedEvent"/>.
    /// </summary>
    /// <param name="reason">A human-readable description of the failure.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the request is not in <see cref="TraceStatus.InProgress"/> state.
    /// </exception>
    public void Fail(string reason)
    {
        if (Status != TraceStatus.InProgress)
            throw new InvalidOperationException(
                $"Cannot fail trace request. Current status: {Status}.");

        ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));

        FailureReason = reason.Length > MaxFailureReasonLength
            ? reason[..MaxFailureReasonLength]
            : reason;
        Status = TraceStatus.Failed;
        SetCompletionTimestamps();

        AddDomainEvent(new TraceCompletedEvent(Id, CompanyProfileId: null, Status, OverallConfidence: null));
    }

    /// <summary>
    /// Transitions the request to <see cref="TraceStatus.Cancelled"/>.
    /// Can be called from <see cref="TraceStatus.Pending"/> or <see cref="TraceStatus.InProgress"/> states.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the request is already in a terminal state.
    /// </exception>
    public void Cancel()
    {
        if (Status is TraceStatus.Completed or TraceStatus.PartiallyCompleted
            or TraceStatus.Failed or TraceStatus.Cancelled)
            throw new InvalidOperationException(
                $"Cannot cancel trace request. Current status: {Status}.");

        Status = TraceStatus.Cancelled;
        SetCompletionTimestamps();
    }

    private const int MaxFailureReasonLength = 2000;

    private bool HasAnyIdentifyingField() =>
        !string.IsNullOrWhiteSpace(CompanyName) ||
        !string.IsNullOrWhiteSpace(RegistrationId) ||
        !string.IsNullOrWhiteSpace(TaxId) ||
        !string.IsNullOrWhiteSpace(Phone) ||
        !string.IsNullOrWhiteSpace(Email) ||
        !string.IsNullOrWhiteSpace(Website);

    private void SetCompletionTimestamps()
    {
        CompletedAt = DateTimeOffset.UtcNow;
        DurationMs = (long)(CompletedAt.Value - CreatedAt).TotalMilliseconds;
    }
}
