using FluentValidation;

namespace Tracer.Application.Commands.AcknowledgeChange;

/// <summary>
/// Validates that the change-event id is non-empty. Existence is checked
/// in the handler and returned as <see cref="AcknowledgeChangeResult.NotFound"/>.
/// </summary>
public sealed class AcknowledgeChangeValidator : AbstractValidator<AcknowledgeChangeCommand>
{
    public AcknowledgeChangeValidator()
    {
        RuleFor(c => c.ChangeEventId)
            .NotEqual(Guid.Empty)
            .WithMessage("ChangeEventId must be a non-empty GUID.");
    }
}
