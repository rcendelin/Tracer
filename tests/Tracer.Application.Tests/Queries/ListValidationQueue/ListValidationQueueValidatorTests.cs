using FluentAssertions;
using FluentValidation.TestHelper;
using Tracer.Application.Queries.ListValidationQueue;

namespace Tracer.Application.Tests.Queries.ListValidationQueue;

public sealed class ListValidationQueueValidatorTests
{
    private readonly ListValidationQueueValidator _sut = new();

    [Fact]
    public void Validate_DefaultQuery_Succeeds()
    {
        var result = _sut.TestValidate(new ListValidationQueueQuery());

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_NegativePage_Fails(int page)
    {
        var result = _sut.TestValidate(new ListValidationQueueQuery { Page = page, PageSize = 20 });

        result.ShouldHaveValidationErrorFor(q => q.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    public void Validate_ValidPage_Succeeds(int page)
    {
        var result = _sut.TestValidate(new ListValidationQueueQuery { Page = page, PageSize = 20 });

        result.ShouldNotHaveValidationErrorFor(q => q.Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(500)]
    public void Validate_PageSizeOutOfRange_Fails(int pageSize)
    {
        var result = _sut.TestValidate(new ListValidationQueueQuery { Page = 0, PageSize = pageSize });

        result.ShouldHaveValidationErrorFor(q => q.PageSize);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(100)]
    public void Validate_PageSizeInRange_Succeeds(int pageSize)
    {
        var result = _sut.TestValidate(new ListValidationQueueQuery { Page = 0, PageSize = pageSize });

        result.ShouldNotHaveValidationErrorFor(q => q.PageSize);
    }
}
