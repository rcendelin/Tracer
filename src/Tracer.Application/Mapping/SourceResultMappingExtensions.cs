using Tracer.Application.DTOs;
using Tracer.Domain.Entities;

namespace Tracer.Application.Mapping;

/// <summary>
/// Extension methods for mapping <see cref="SourceResult"/> to DTOs.
/// </summary>
public static class SourceResultMappingExtensions
{
    public static SourceResultDto ToDto(this SourceResult sourceResult)
    {
        ArgumentNullException.ThrowIfNull(sourceResult);
        return new SourceResultDto
        {
            ProviderId = sourceResult.ProviderId,
        Status = sourceResult.Status,
        FieldsEnriched = sourceResult.FieldsEnriched,
        DurationMs = sourceResult.DurationMs,
            ErrorMessage = sourceResult.ErrorMessage,
        };
    }
}
