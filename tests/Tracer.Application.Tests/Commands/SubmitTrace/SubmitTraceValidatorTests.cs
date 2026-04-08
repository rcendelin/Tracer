using FluentAssertions;
using FluentValidation.TestHelper;
using Tracer.Application.Commands.SubmitTrace;
using Tracer.Application.DTOs;
using Tracer.Domain.Enums;

namespace Tracer.Application.Tests.Commands.SubmitTrace;

public sealed class SubmitTraceValidatorTests
{
    private readonly SubmitTraceValidator _sut = new();

    private static SubmitTraceCommand CreateValidCommand() => new()
    {
        Input = new TraceRequestDto
        {
            CompanyName = "Acme s.r.o.",
            Country = "CZ",
            Depth = TraceDepth.Standard,
        },
        Source = "rest-api",
    };

    [Fact]
    public void ValidCommand_PassesValidation()
    {
        var result = _sut.TestValidate(CreateValidCommand());
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void NoIdentifyingFields_FailsValidation()
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto
            {
                Country = "CZ",
                Depth = TraceDepth.Standard,
            },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Input);
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("identifying field"));
    }

    [Fact]
    public void OnlyRegistrationId_PassesValidation()
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { RegistrationId = "12345678" },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Input);
    }

    [Theory]
    [InlineData("CZ")]
    [InlineData("GB")]
    [InlineData("US")]
    [InlineData("DE")]
    public void ValidCountryCode_PassesValidation(string country)
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { CompanyName = "Test", Country = country },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Input.Country);
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("ZZZ")]
    [InlineData("1")]
    public void InvalidCountryCode_FailsValidation(string country)
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { CompanyName = "Test", Country = country },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Input.Country);
    }

    [Fact]
    public void NullCountry_PassesValidation()
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { CompanyName = "Test", Country = null },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Input.Country);
    }

    [Fact]
    public void HttpCallbackUrl_FailsValidation()
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto
            {
                CompanyName = "Test",
                CallbackUrl = new Uri("http://insecure.example.com"),
            },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Input.CallbackUrl);
    }

    [Fact]
    public void HttpsCallbackUrl_PassesValidation()
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto
            {
                CompanyName = "Test",
                CallbackUrl = new Uri("https://secure.example.com/hook"),
            },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Input.CallbackUrl);
    }

    [Fact]
    public void EmptySource_FailsValidation()
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { CompanyName = "Test" },
            Source = "",
        };

        var result = _sut.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Source);
    }

    [Fact]
    public void CompanyNameTooLong_FailsValidation()
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { CompanyName = new string('x', 501) },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Input.CompanyName);
    }

    // ── Website URL validation ─────────────────────────────────────────────────

    [Theory]
    [InlineData("https://www.example.com")]
    [InlineData("http://example.com/path")]
    [InlineData("https://sub.domain.cz/page?q=1")]
    public void ValidWebsiteUrl_PassesValidation(string website)
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { Website = website },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Input.Website);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://files.example.com/data")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//missing-scheme.com")]
    public void InvalidWebsiteUrl_FailsValidation(string website)
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { CompanyName = "Fallback Name", Website = website },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Input.Website);
    }

    [Fact]
    public void NullWebsite_PassesValidation()
    {
        var command = new SubmitTraceCommand
        {
            Input = new TraceRequestDto { CompanyName = "Acme", Website = null },
            Source = "rest-api",
        };

        var result = _sut.TestValidate(command);
        result.ShouldNotHaveValidationErrorFor(x => x.Input.Website);
    }
}
