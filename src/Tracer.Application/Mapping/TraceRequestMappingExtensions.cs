using Tracer.Application.DTOs;
using Tracer.Domain.Entities;

namespace Tracer.Application.Mapping;

/// <summary>
/// Extension methods for mapping <see cref="TraceRequest"/> to DTOs.
/// </summary>
public static class TraceRequestMappingExtensions
{
    public static TraceResultDto ToResultDto(this TraceRequest request, CompanyProfile? profile = null, IReadOnlyCollection<SourceResultDto>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new TraceResultDto
        {
            TraceId = request.Id,
        Status = request.Status,
        Company = profile?.ToEnrichedDto(),
        Sources = sources,
        OverallConfidence = request.OverallConfidence?.Value,
        CreatedAt = request.CreatedAt,
        CompletedAt = request.CompletedAt,
        DurationMs = request.DurationMs,
            FailureReason = request.FailureReason,
        };
    }
}
