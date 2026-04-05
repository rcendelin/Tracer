using MediatR;
using Tracer.Application.DTOs;

namespace Tracer.Application.Commands.SubmitTrace;

/// <summary>
/// Command to submit a new enrichment trace request.
/// </summary>
public sealed record SubmitTraceCommand : IRequest<TraceResultDto>
{
    /// <summary>Gets the input data for the trace request.</summary>
    public required TraceRequestDto Input { get; init; }

    /// <summary>
    /// Gets the source of the request.
    /// Expected values: <c>"rest-api"</c>, <c>"service-bus"</c>, <c>"ui"</c>, <c>"revalidation"</c>.
    /// </summary>
    public string Source { get; init; } = "rest-api";
}
