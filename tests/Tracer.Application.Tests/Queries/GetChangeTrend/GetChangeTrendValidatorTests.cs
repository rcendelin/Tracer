using FluentAssertions;
using FluentValidation.TestHelper;
using Tracer.Application.Queries.GetChangeTrend;

namespace Tracer.Application.Tests.Queries.GetChangeTrend;

public sealed class GetChangeTrendValidatorTests
{
    private readonly GetChangeTrendValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(36)]
    public void Valid_MonthsInRange_Passes(int months)
    {
        var result = _validator.TestValidate(new GetChangeTrendQuery(TrendPeriod.Monthly, months));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(37)]
    [InlineData(100)]
    public void Invalid_MonthsOutOfRange_Fails(int months)
    {
        var result = _validator.TestValidate(new GetChangeTrendQuery(TrendPeriod.Monthly, months));

        result.ShouldHaveValidationErrorFor(q => q.Months);
    }

    [Fact]
    public void Invalid_PeriodOutOfEnum_Fails()
    {
        var result = _validator.TestValidate(new GetChangeTrendQuery((TrendPeriod)99, 12));

        result.ShouldHaveValidationErrorFor(q => q.Period);
    }
}
