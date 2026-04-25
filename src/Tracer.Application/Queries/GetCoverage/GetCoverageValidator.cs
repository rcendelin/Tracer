using FluentValidation;

namespace Tracer.Application.Queries.GetCoverage;

/// <summary>
/// Validates <see cref="GetCoverageQuery"/> parameters supplied by the caller.
/// </summary>
public sealed class GetCoverageValidator : AbstractValidator<GetCoverageQuery>
{
    public GetCoverageValidator()
    {
        RuleFor(q => q.GroupBy).IsInEnum();
    }
}
