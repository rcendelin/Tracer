using FluentValidation;

namespace Tracer.Application.Queries.ListValidationQueue;

/// <summary>
/// Validates <see cref="ListValidationQueueQuery"/> parameters supplied by the caller.
/// </summary>
public sealed class ListValidationQueueValidator : AbstractValidator<ListValidationQueueQuery>
{
    public ListValidationQueueValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}
