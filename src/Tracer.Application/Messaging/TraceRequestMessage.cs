using Tracer.Application.DTOs;

namespace Tracer.Application.Messaging;

/// <summary>
/// Message received from the <c>tracer-request</c> queue.
/// Sent by FieldForce or other upstream services to request enrichment.
/// </summary>
public sealed record TraceRequestMessage
{
    /// <summary>Correlation ID for request-reply pattern.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>The trace request input.</summary>
    public required TraceRequestDto Input { get; init; }

    /// <summary>Source of the request (e.g. "service-bus", "fieldforce").</summary>
    public string Source { get; init; } = "service-bus";
}
