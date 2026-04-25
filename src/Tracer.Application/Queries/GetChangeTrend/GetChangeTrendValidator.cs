using FluentValidation;

namespace Tracer.Application.Queries.GetChangeTrend;

/// <summary>
/// Validates <see cref="GetChangeTrendQuery"/> parameters supplied by the caller.
/// </summary>
public sealed class GetChangeTrendValidator : AbstractValidator<GetChangeTrendQuery>
{
    public GetChangeTrendValidator()
    {
        RuleFor(q => q.Period).IsInEnum();
        RuleFor(q => q.Months).InclusiveBetween(1, 36);
    }
}
