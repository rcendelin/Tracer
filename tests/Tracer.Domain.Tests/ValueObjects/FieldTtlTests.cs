using FluentAssertions;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;

namespace Tracer.Domain.Tests.ValueObjects;

public sealed class FieldTtlTests
{
    [Fact]
    public void For_EntityStatus_Returns30Days()
    {
        var ttl = FieldTtl.For(FieldName.EntityStatus);
        ttl.Ttl.Should().Be(TimeSpan.FromDays(30));
        ttl.Field.Should().Be(FieldName.EntityStatus);
    }

    [Fact]
    public void For_Officers_Returns90Days()
    {
        var ttl = FieldTtl.For(FieldName.Officers);
        ttl.Ttl.Should().Be(TimeSpan.FromDays(90));
    }

    [Theory]
    [InlineData(FieldName.Phone)]
    [InlineData(FieldName.Email)]
    [InlineData(FieldName.Website)]
    public void For_CommunicationFields_Return180Days(FieldName field)
    {
        var ttl = FieldTtl.For(field);
        ttl.Ttl.Should().Be(TimeSpan.FromDays(180));
    }

    [Theory]
    [InlineData(FieldName.RegisteredAddress)]
    [InlineData(FieldName.OperatingAddress)]
    public void For_AddressFields_Return365Days(FieldName field)
    {
        var ttl = FieldTtl.For(field);
        ttl.Ttl.Should().Be(TimeSpan.FromDays(365));
    }

    [Theory]
    [InlineData(FieldName.RegistrationId)]
    [InlineData(FieldName.TaxId)]
    public void For_RegistrationFields_Return730Days(FieldName field)
    {
        var ttl = FieldTtl.For(field);
        ttl.Ttl.Should().Be(TimeSpan.FromDays(730));
    }

    [Theory]
    [InlineData(FieldName.LegalName)]
    [InlineData(FieldName.TradeName)]
    [InlineData(FieldName.LegalForm)]
    [InlineData(FieldName.Industry)]
    [InlineData(FieldName.EmployeeRange)]
    [InlineData(FieldName.ParentCompany)]
    [InlineData(FieldName.Location)]
    public void For_OtherFields_ReturnDefault180Days(FieldName field)
    {
        var ttl = FieldTtl.For(field);
        ttl.Ttl.Should().Be(TimeSpan.FromDays(180));
    }

    [Fact]
    public void Custom_ReturnsSpecifiedTtl()
    {
        var customTtl = TimeSpan.FromDays(7);
        var ttl = FieldTtl.Custom(FieldName.Phone, customTtl);
        ttl.Ttl.Should().Be(customTtl);
        ttl.Field.Should().Be(FieldName.Phone);
    }

    [Fact]
    public void ValueEquality_TwoIdenticalFieldTtls_AreEqual()
    {
        var a = FieldTtl.For(FieldName.EntityStatus);
        var b = FieldTtl.For(FieldName.EntityStatus);
        a.Should().Be(b);
    }
}
