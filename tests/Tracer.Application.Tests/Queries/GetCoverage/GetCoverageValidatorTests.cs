using FluentAssertions;
using FluentValidation.TestHelper;
using Tracer.Application.Queries.GetCoverage;

namespace Tracer.Application.Tests.Queries.GetCoverage;

public sealed class GetCoverageValidatorTests
{
    private readonly GetCoverageValidator _validator = new();

    [Fact]
    public void Valid_Country_Passes()
    {
        var result = _validator.TestValidate(new GetCoverageQuery(CoverageGroupBy.Country));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Invalid_GroupByOutOfEnum_Fails()
    {
        var result = _validator.TestValidate(new GetCoverageQuery((CoverageGroupBy)99));

        result.ShouldHaveValidationErrorFor(q => q.GroupBy);
    }
}
