using FluentValidation;

namespace Tracer.Application.Queries.ListProfiles;

/// <summary>
/// Validates a <see cref="ListProfilesQuery"/> before it reaches the handler.
/// </summary>
public sealed class ListProfilesValidator : AbstractValidator<ListProfilesQuery>
{
    public ListProfilesValidator()
    {
        RuleFor(q => q.Search)
            .MaximumLength(200)
            .When(q => q.Search is not null);

        RuleFor(q => q.Country)
            .Length(2)
            .Matches("^[A-Za-z]{2}$")
            .When(q => q.Country is not null);

        RuleFor(q => q.MinConfidence)
            .InclusiveBetween(0.0, 1.0)
            .When(q => q.MinConfidence.HasValue);

        RuleFor(q => q.MaxConfidence)
            .InclusiveBetween(0.0, 1.0)
            .When(q => q.MaxConfidence.HasValue);

        RuleFor(q => q)
            .Must(q => q.MinConfidence is null || q.MaxConfidence is null || q.MinConfidence <= q.MaxConfidence)
            .WithMessage("MinConfidence must be less than or equal to MaxConfidence.")
            .When(q => q.MinConfidence.HasValue && q.MaxConfidence.HasValue);
    }
}
