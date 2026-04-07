using FluentValidation;

namespace Tracer.Application.Queries.ListChanges;

/// <summary>
/// Validates <see cref="ListChangesQuery"/> parameters supplied by the caller.
/// </summary>
public sealed class ListChangesValidator : AbstractValidator<ListChangesQuery>
{
    public ListChangesValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
