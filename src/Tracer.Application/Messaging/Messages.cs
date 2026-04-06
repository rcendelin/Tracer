using Tracer.Application.DTOs;

namespace Tracer.Application.Messaging;

/// <summary>
/// Message sent to the <c>tracer-response</c> queue when a trace completes.
/// Consumed by FieldForce or other downstream services.
/// </summary>
public sealed record TraceResponseMessage
{
    /// <summary>Correlation ID from the original request (for Service Bus request-reply pattern).</summary>
    public required string CorrelationId { get; init; }

    /// <summary>The enrichment result.</summary>
    public required TraceResultDto Result { get; init; }
}

/// <summary>
/// Message sent to the <c>tracer-changes</c> topic when a field change is detected.
/// Consumed by FieldForce for CRM data synchronization.
/// </summary>
public sealed record ChangeEventMessage
{
    /// <summary>The change event details.</summary>
    public required ChangeEventDto ChangeEvent { get; init; }

    /// <summary>The company profile ID that changed.</summary>
    public required Guid CompanyProfileId { get; init; }

    /// <summary>The normalized key of the affected profile.</summary>
    public required string NormalizedKey { get; init; }
}
