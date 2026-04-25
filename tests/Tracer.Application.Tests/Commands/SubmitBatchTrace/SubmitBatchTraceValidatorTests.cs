using FluentAssertions;
using FluentValidation.TestHelper;
using Tracer.Application.Commands.SubmitBatchTrace;
using Tracer.Application.DTOs;
using Tracer.Domain.Enums;

namespace Tracer.Application.Tests.Commands.SubmitBatchTrace;

public sealed class SubmitBatchTraceValidatorTests
{
    private readonly SubmitBatchTraceValidator _sut = new();

    private static TraceRequestDto ValidItem(
        string companyName = "Acme s.r.o.",
        string country = "CZ",
        string? registrationId = "12345678") =>
        new()
        {
            CompanyName = companyName,
            Country = country,
            RegistrationId = registrationId,
            Depth = TraceDepth.Standard,
        };

    private static SubmitBatchTraceCommand ValidCommand(
        IReadOnlyCollection<TraceRequestDto>? items = null) =>
        new()
        {
            Items = items ?? [ValidItem()],
        };

    [Fact]
    public async Task ValidSingleItem_PassesValidation()
    {
        var result = await _sut.TestValidateAsync(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task EmptyItems_FailsValidation()
    {
        var command = ValidCommand([]);

        var result = await _sut.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.Items);
    }

    [Fact]
    public async Task Over200Items_FailsValidation()
    {
        var items = Enumerable.Range(0, 201)
            .Select(i => ValidItem($"Company {i}", "CZ", i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        var command = ValidCommand(items);

        var result = await _sut.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor(c => c.Items);
    }

    [Fact]
    public async Task Exactly200Items_PassesValidation()
    {
        var items = Enumerable.Range(0, 200)
            .Select(i => ValidItem($"Company {i}", "CZ", i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

        var command = ValidCommand(items);

        var result = await _sut.TestValidateAsync(command);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task ItemWithAllIdentifyingFieldsEmpty_FailsValidation()
    {
        // CompanyName counts as identifying field — must be empty too
        var item = new TraceRequestDto
        {
            CompanyName = "",
            Country = "CZ",
            RegistrationId = null,
            TaxId = null,
            Phone = null,
            Email = null,
            Website = null,
            Depth = TraceDepth.Standard,
        };

        var command = ValidCommand([item]);

        var result = await _sut.TestValidateAsync(command);

        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ItemWithInvalidCountryCode_FailsValidation()
    {
        var command = ValidCommand([ValidItem(country: "INVALID")]);

        var result = await _sut.TestValidateAsync(command);

        result.ShouldHaveValidationErrorFor("Items[0].Country");
    }

    [Fact]
    public async Task ItemWithCompanyNameOnly_PassesIdentifyingFieldCheck()
    {
        // CompanyName alone is a sufficient identifying field
        var item = new TraceRequestDto
        {
            CompanyName = "Acme s.r.o.",
            Country = "CZ",
            RegistrationId = null,
            TaxId = null,
            Phone = null,
            Email = null,
            Website = null,
            Depth = TraceDepth.Standard,
        };

        var result = await _sut.TestValidateAsync(ValidCommand([item]));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task ItemWithRegistrationIdOnly_PassesIdentifyingFieldCheck()
    {
        var item = ValidItem(registrationId: "12345678");

        var result = await _sut.TestValidateAsync(ValidCommand([item]));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public async Task ItemWithPhoneAsIdentifyingField_PassesValidation()
    {
        var item = new TraceRequestDto
        {
            CompanyName = "Acme",
            Country = "CZ",
            Phone = "+420123456789",
            Depth = TraceDepth.Standard,
        };

        var result = await _sut.TestValidateAsync(ValidCommand([item]));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
