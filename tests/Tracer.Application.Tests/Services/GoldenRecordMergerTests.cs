using FluentAssertions;
using Tracer.Application.Services;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Services;

public sealed class GoldenRecordMergerTests
{
    private readonly GoldenRecordMerger _sut = new(new ConfidenceScorer());

    private static ProviderMergeInput CreateInput(
        string providerId,
        double sourceQuality,
        Dictionary<FieldName, object?> fields)
    {
        return new ProviderMergeInput
        {
            ProviderId = providerId,
            SourceQuality = sourceQuality,
            Result = ProviderResult.Success(fields, TimeSpan.FromMilliseconds(100)),
        };
    }

    [Fact]
    public void Merge_ThreeProvidersAgreeOnField_HighConfidence()
    {
        var inputs = new[]
        {
            CreateInput("ares", 0.95, new() { [FieldName.LegalName] = "Acme s.r.o." }),
            CreateInput("gleif-lei", 0.85, new() { [FieldName.LegalName] = "Acme s.r.o." }),
            CreateInput("google-maps", 0.70, new() { [FieldName.LegalName] = "Acme s.r.o." }),
        };

        var result = _sut.Merge(inputs);

        result.BestFields.Should().ContainKey(FieldName.LegalName);
        result.BestFields[FieldName.LegalName].Confidence.Value.Should().BeGreaterThan(0.7);
        ((string)result.BestFields[FieldName.LegalName].Value).Should().Be("Acme s.r.o.");
    }

    [Fact]
    public void Merge_ConflictOnAddress_RegistryWinsOverGeo()
    {
        var registryAddress = new Address
        {
            Street = "tř. Václava Klementa 869",
            City = "Mladá Boleslav",
            PostalCode = "29301",
            Country = "CZ",
        };
        var geoAddress = new Address
        {
            Street = "Václava Klementa",
            City = "Mladá Boleslav",
            PostalCode = "293 01",
            Country = "CZ",
        };

        var inputs = new[]
        {
            CreateInput("ares", 0.95, new() { [FieldName.RegisteredAddress] = registryAddress }),
            CreateInput("google-maps", 0.70, new() { [FieldName.RegisteredAddress] = geoAddress }),
        };

        var result = _sut.Merge(inputs);

        result.BestFields.Should().ContainKey(FieldName.RegisteredAddress);
        // ARES (official registry, verification 1.0) should win over Google Maps (geo, 0.7)
        result.BestFields[FieldName.RegisteredAddress].Source.Should().Be("ares");
    }

    [Fact]
    public void Merge_SingleProvider_ReturnsItsFields()
    {
        var inputs = new[]
        {
            CreateInput("ares", 0.95, new()
            {
                [FieldName.LegalName] = "Acme",
                [FieldName.TaxId] = "CZ12345678",
            }),
        };

        var result = _sut.Merge(inputs);

        result.BestFields.Should().HaveCount(2);
        result.BestFields.Should().ContainKey(FieldName.LegalName);
        result.BestFields.Should().ContainKey(FieldName.TaxId);
    }

    [Fact]
    public void Merge_NoSuccessfulProviders_ReturnsEmpty()
    {
        var inputs = new[]
        {
            new ProviderMergeInput
            {
                ProviderId = "ares",
                SourceQuality = 0.95,
                Result = ProviderResult.NotFound(TimeSpan.FromMilliseconds(50)),
            },
        };

        var result = _sut.Merge(inputs);

        result.BestFields.Should().BeEmpty();
        result.CandidateValues.Should().BeEmpty();
    }

    [Fact]
    public void Merge_RetainsCandidateValuesForAudit()
    {
        var inputs = new[]
        {
            CreateInput("ares", 0.95, new() { [FieldName.Phone] = "+420111" }),
            CreateInput("google-maps", 0.70, new() { [FieldName.Phone] = "+420222" }),
        };

        var result = _sut.Merge(inputs);

        result.CandidateValues.Should().ContainKey(FieldName.Phone);
        result.CandidateValues[FieldName.Phone].Should().HaveCount(2);
    }

    [Fact]
    public void Merge_MultipleFieldsFromDifferentProviders_MergesAll()
    {
        var inputs = new[]
        {
            CreateInput("ares", 0.95, new()
            {
                [FieldName.LegalName] = "Acme s.r.o.",
                [FieldName.TaxId] = "CZ12345678",
            }),
            CreateInput("google-maps", 0.70, new()
            {
                [FieldName.Phone] = "+420111",
                [FieldName.Website] = "https://acme.cz",
            }),
        };

        var result = _sut.Merge(inputs);

        result.BestFields.Should().HaveCount(4);
        result.BestFields.Should().ContainKeys(
            FieldName.LegalName, FieldName.TaxId, FieldName.Phone, FieldName.Website);
    }

    [Fact]
    public void Merge_NullFieldValues_AreSkipped()
    {
        var inputs = new[]
        {
            CreateInput("ares", 0.95, new()
            {
                [FieldName.LegalName] = "Acme",
                [FieldName.Phone] = null,
            }),
        };

        var result = _sut.Merge(inputs);

        result.BestFields.Should().ContainKey(FieldName.LegalName);
        result.BestFields.Should().NotContainKey(FieldName.Phone);
    }

    [Fact]
    public void Merge_EmptyInput_ReturnsEmpty()
    {
        var result = _sut.Merge([]);

        result.BestFields.Should().BeEmpty();
        result.CandidateValues.Should().BeEmpty();
    }
}
