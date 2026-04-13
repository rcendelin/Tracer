using FluentAssertions;
using NSubstitute;
using Tracer.Application.DTOs;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Services;

public sealed class EntityResolverTests
{
    private readonly ICompanyProfileRepository _repo = Substitute.For<ICompanyProfileRepository>();
    private readonly ICompanyNameNormalizer _normalizer = new CompanyNameNormalizer();
    private EntityResolver CreateSut() => new(_repo, _normalizer);

    // ── NormalizeName ───────────────────────────────────────────────

    [Fact]
    public void NormalizeName_SkodaAutoVariants_ProduceSameResult()
    {
        var v1 = EntityResolver.NormalizeName("ŠKODA AUTO a.s.");
        var v2 = EntityResolver.NormalizeName("Škoda Auto, a.s.");
        var v3 = EntityResolver.NormalizeName("škoda auto a.s");

        v1.Should().Be(v2);
        v2.Should().Be(v3);
    }

    [Fact]
    public void NormalizeName_RemovesDiacritics()
    {
        var result = EntityResolver.NormalizeName("Průmyslové závody Černošice");

        result.Should().NotContain("Ů");
        result.Should().NotContain("Č");
        result.Should().Contain("PRUMYSLOVE");
    }

    [Fact]
    public void NormalizeName_RemovesLegalForms()
    {
        EntityResolver.NormalizeName("Acme GmbH").Should().NotContain("GMBH");
        EntityResolver.NormalizeName("Test LLC").Should().NotContain("LLC");
        EntityResolver.NormalizeName("Firma Ltd.").Should().NotContain("LTD");
        EntityResolver.NormalizeName("Corp s.r.o.").Should().NotContain("SRO");
    }

    [Fact]
    public void NormalizeName_SortsTokens()
    {
        var v1 = EntityResolver.NormalizeName("Alpha Beta");
        var v2 = EntityResolver.NormalizeName("Beta Alpha");

        v1.Should().Be(v2);
    }

    [Fact]
    public void NormalizeName_RemovesPunctuation()
    {
        var result = EntityResolver.NormalizeName("Acme, Inc. (CZ)");

        result.Should().NotContain(",");
        result.Should().NotContain("(");
    }

    // ── GenerateNormalizedKey ────────────────────────────────────────

    [Fact]
    public void GenerateNormalizedKey_WithRegistrationId_ReturnsCountryColon()
    {
        var sut = CreateSut();

        var key = sut.GenerateNormalizedKey("Acme", "CZ", "12345678");

        key.Should().Be("CZ:12345678");
    }

    [Fact]
    public void GenerateNormalizedKey_WithNameOnly_ReturnsNameHash()
    {
        var sut = CreateSut();

        var key = sut.GenerateNormalizedKey("Škoda Auto a.s.", "CZ", null);

        key.Should().StartWith("NAME:CZ:");
        key.Should().HaveLength("NAME:CZ:".Length + 16); // 16 hex chars
    }

    [Fact]
    public void GenerateNormalizedKey_SameNameDifferentForm_SameHash()
    {
        var sut = CreateSut();

        var key1 = sut.GenerateNormalizedKey("ŠKODA AUTO a.s.", "CZ", null);
        var key2 = sut.GenerateNormalizedKey("Škoda Auto, a.s.", "CZ", null);

        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateNormalizedKey_NoNameNoId_ReturnsUnknown()
    {
        var sut = CreateSut();

        var key = sut.GenerateNormalizedKey(null, "CZ", null);

        key.Should().StartWith("UNKNOWN:");
    }

    // ── ResolveAsync ────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_ByRegistrationId_ReturnsExisting()
    {
        var sut = CreateSut();
        var existing = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        _repo.FindByRegistrationIdAsync("12345678", "CZ", Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            RegistrationId = "12345678",
            Country = "CZ",
            CompanyName = "Acme",
        }, CancellationToken.None);

        result.Should().Be(existing);
    }

    [Fact]
    public async Task ResolveAsync_ByNormalizedKey_ReturnsExisting()
    {
        var sut = CreateSut();
        var key = sut.GenerateNormalizedKey("Škoda Auto", "CZ", null);
        var existing = new CompanyProfile(key, "CZ");
        _repo.FindByKeyAsync(key, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "ŠKODA AUTO a.s.",
            Country = "CZ",
        }, CancellationToken.None);

        result.Should().Be(existing);
    }

    [Fact]
    public async Task ResolveAsync_NoMatch_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.ResolveAsync(new TraceRequestDto
        {
            CompanyName = "NonExistent Corp",
            Country = "GB",
        }, CancellationToken.None);

        result.Should().BeNull();
    }
}
