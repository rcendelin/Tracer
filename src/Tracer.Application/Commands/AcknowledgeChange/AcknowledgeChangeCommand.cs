using MediatR;

namespace Tracer.Application.Commands.AcknowledgeChange;

/// <summary>
/// Marks a <see cref="Tracer.Domain.Entities.ChangeEvent"/> as acknowledged
/// (notified). Idempotent: a second invocation on an already-acknowledged
/// event still returns <see cref="AcknowledgeChangeResult.Acknowledged"/>.
/// </summary>
/// <param name="ChangeEventId">The change event identifier.</param>
public sealed record AcknowledgeChangeCommand(Guid ChangeEventId) : IRequest<AcknowledgeChangeResult>;

/// <summary>
/// Outcome of an <see cref="AcknowledgeChangeCommand"/>.
/// </summary>
public enum AcknowledgeChangeResult
{
    /// <summary>The change event was acknowledged (or already had been).</summary>
    Acknowledged = 0,

    /// <summary>No change event with the given id exists.</summary>
    NotFound = 1,
}
