using FluentValidation;
using Tracer.Domain.Enums;

namespace Tracer.Application.Commands.OverrideField;

/// <summary>
/// Validates an <see cref="OverrideFieldCommand"/>. Field-eligibility is
/// re-checked in the handler against the whitelist (this validator handles
/// only payload shape).
/// </summary>
public sealed class OverrideFieldValidator : AbstractValidator<OverrideFieldCommand>
{
    public const int MaxValueLength = 4000;
    public const int MaxReasonLength = 2000;

    public OverrideFieldValidator()
    {
        RuleFor(c => c.ProfileId)
            .NotEqual(Guid.Empty)
            .WithMessage("ProfileId must be a non-empty GUID.");

        RuleFor(c => c.Field)
            .IsInEnum()
            .WithMessage("Field must be a defined FieldName member.");

        RuleFor(c => c.NewValue)
            .NotNull().WithMessage("NewValue is required.")
            .Must(v => !string.IsNullOrWhiteSpace(v)).WithMessage("NewValue must not be whitespace-only.")
            .MaximumLength(MaxValueLength)
            .WithMessage($"NewValue must be ≤ {MaxValueLength} characters.");

        RuleFor(c => c.Reason)
            .NotNull().WithMessage("Reason is required for manual overrides (audit trail).")
            .Must(r => !string.IsNullOrWhiteSpace(r)).WithMessage("Reason must not be whitespace-only.")
            .MaximumLength(MaxReasonLength)
            .WithMessage($"Reason must be ≤ {MaxReasonLength} characters.");

        RuleFor(c => c.CallerFingerprint)
            .NotEmpty()
            .WithMessage("CallerFingerprint is required (server-derived from API key middleware).");
    }
}
