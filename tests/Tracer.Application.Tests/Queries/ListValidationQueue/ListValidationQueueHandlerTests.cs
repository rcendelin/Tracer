using FluentAssertions;
using NSubstitute;
using Tracer.Application.Queries.ListValidationQueue;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Domain.ValueObjects;

namespace Tracer.Application.Tests.Queries.ListValidationQueue;

public sealed class ListValidationQueueHandlerTests
{
    private readonly ICompanyProfileRepository _profiles = Substitute.For<ICompanyProfileRepository>();
    private readonly IFieldTtlPolicy _ttlPolicy = Substitute.For<IFieldTtlPolicy>();

    private ListValidationQueueHandler CreateSut() => new(_profiles, _ttlPolicy);

    private static CompanyProfile CreateProfile(
        string? legalName = "ACME Corp",
        double? overallConfidence = 0.87)
    {
        var profile = new CompanyProfile(
            normalizedKey: $"CZ:{Guid.NewGuid():N}",
            country: "CZ",
            registrationId: "12345678");

        if (legalName is not null)
        {
            profile.UpdateField(
                FieldName.LegalName,
                new TracedField<string>
                {
                    Value = legalName,
                    Confidence = Confidence.Create(0.9),
                    Source = "ares",
                    EnrichedAt = DateTimeOffset.UtcNow.AddDays(-1),
                },
                source: "ares");
        }

        if (overallConfidence is not null)
            profile.SetOverallConfidence(Confidence.Create(overallConfidence.Value));

        return profile;
    }

    // ── Null guard ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NullRequest_ThrowsArgumentNullException()
    {
        var act = () => CreateSut().Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Paging clamps ──────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NegativePage_ClampsToZero()
    {
        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(0);

        var result = await CreateSut().Handle(
            new ListValidationQueueQuery { Page = -3, PageSize = 20 }, CancellationToken.None);

        result.Page.Should().Be(0);
    }

    [Fact]
    public async Task Handle_PageSizeZero_ClampsToOne()
    {
        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(0);

        var result = await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 0, PageSize = 0 }, CancellationToken.None);

        result.PageSize.Should().Be(1);
    }

    [Fact]
    public async Task Handle_PageSizeOver100_ClampsTo100()
    {
        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(0);

        var result = await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 0, PageSize = 999 }, CancellationToken.None);

        result.PageSize.Should().Be(100);
    }

    // ── Sweep cap passed to repository ─────────────────────────────────────

    [Fact]
    public async Task Handle_SweepCap_IsPageTimesPageSize()
    {
        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(0);

        await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 2, PageSize = 20 }, CancellationToken.None);

        // (page + 1) * pageSize = 3 * 20 = 60
        await _profiles.Received(1).GetRevalidationQueueAsync(60, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SweepCap_IsBoundedByMaxQueueSweep()
    {
        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(0);

        // page=100, pageSize=100 → naive cap 10_100, clamped to MaxQueueSweep=500.
        await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 100, PageSize = 100 }, CancellationToken.None);

        await _profiles.Received(1).GetRevalidationQueueAsync(500, Arg.Any<CancellationToken>());
    }

    // ── Filter: only profiles with expired fields make the slice ───────────

    [Fact]
    public async Task Handle_FiltersOutProfilesWithoutExpiredFields()
    {
        var expiredProfile = CreateProfile(legalName: "Expired Co");
        var freshProfile = CreateProfile(legalName: "Fresh Co");

        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([expiredProfile, freshProfile]);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(2);

        _ttlPolicy.GetExpiredFields(expiredProfile, Arg.Any<DateTimeOffset>())
            .Returns([FieldName.EntityStatus]);
        _ttlPolicy.GetExpiredFields(freshProfile, Arg.Any<DateTimeOffset>())
            .Returns(Array.Empty<FieldName>());
        _ttlPolicy.GetNextExpirationDate(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(DateTimeOffset.UtcNow.AddDays(7));

        var result = await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 0, PageSize = 20 }, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items.Single().LegalName.Should().Be("Expired Co");
        result.Items.Single().ExpiredFields.Should().ContainSingle().Which.Should().Be(FieldName.EntityStatus);
    }

    // ── TotalCount comes from repository (approximation) ───────────────────

    [Fact]
    public async Task Handle_TotalCount_ComesFromCountRevalidationCandidatesAsync()
    {
        var p = CreateProfile();
        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([p]);
        _ttlPolicy.GetExpiredFields(p, Arg.Any<DateTimeOffset>())
            .Returns([FieldName.Phone]);
        _ttlPolicy.GetNextExpirationDate(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(DateTimeOffset.UtcNow.AddDays(1));

        // Total upper bound is 137 even though only 1 is actually expired in this sweep.
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(137);

        var result = await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 0, PageSize = 20 }, CancellationToken.None);

        result.TotalCount.Should().Be(137);
        result.Items.Should().HaveCount(1);
    }

    // ── DTO mapping ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MapsProfileToDto_IncludingNextExpiryAndConfidence()
    {
        var profile = CreateProfile(legalName: "Mapped Co", overallConfidence: 0.73);
        var nextExpiry = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([profile]);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _ttlPolicy.GetExpiredFields(profile, Arg.Any<DateTimeOffset>())
            .Returns([FieldName.EntityStatus, FieldName.Phone]);
        _ttlPolicy.GetNextExpirationDate(profile, Arg.Any<DateTimeOffset>())
            .Returns(nextExpiry);

        var result = await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 0, PageSize = 20 }, CancellationToken.None);

        var dto = result.Items.Single();
        dto.ProfileId.Should().Be(profile.Id);
        dto.NormalizedKey.Should().Be(profile.NormalizedKey);
        dto.Country.Should().Be("CZ");
        dto.RegistrationId.Should().Be("12345678");
        dto.LegalName.Should().Be("Mapped Co");
        dto.OverallConfidence.Should().Be(0.73);
        dto.NextFieldExpiryDate.Should().Be(nextExpiry);
        dto.ExpiredFields.Should().BeEquivalentTo(new[] { FieldName.EntityStatus, FieldName.Phone });
    }

    [Fact]
    public async Task Handle_ProfileWithoutLegalName_MapsNullLegalName()
    {
        var profile = CreateProfile(legalName: null, overallConfidence: null);
        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([profile]);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _ttlPolicy.GetExpiredFields(profile, Arg.Any<DateTimeOffset>())
            .Returns([FieldName.RegisteredAddress]);
        _ttlPolicy.GetNextExpirationDate(profile, Arg.Any<DateTimeOffset>())
            .Returns((DateTimeOffset?)null);

        var result = await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 0, PageSize = 20 }, CancellationToken.None);

        var dto = result.Items.Single();
        dto.LegalName.Should().BeNull();
        dto.OverallConfidence.Should().BeNull();
        dto.NextFieldExpiryDate.Should().BeNull();
    }

    // ── Pagination over the filtered set ───────────────────────────────────

    [Fact]
    public async Task Handle_PaginatesFilteredSlice()
    {
        var profiles = Enumerable.Range(0, 15)
            .Select(i => CreateProfile(legalName: $"Co {i}"))
            .ToList();

        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(profiles);
        _profiles.CountRevalidationCandidatesAsync(Arg.Any<CancellationToken>()).Returns(15);

        // Every profile has expired fields.
        _ttlPolicy.GetExpiredFields(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns([FieldName.Phone]);
        _ttlPolicy.GetNextExpirationDate(Arg.Any<CompanyProfile>(), Arg.Any<DateTimeOffset>())
            .Returns(DateTimeOffset.UtcNow.AddDays(1));

        var page1 = await CreateSut().Handle(
            new ListValidationQueueQuery { Page = 1, PageSize = 10 }, CancellationToken.None);

        // Page 1 (zero-based) with pageSize 10 → items 10..14.
        page1.Items.Should().HaveCount(5);
        page1.Items.First().LegalName.Should().Be("Co 10");
    }

    // ── Cancellation ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_CancelledToken_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync().ConfigureAwait(true);

        _profiles.GetRevalidationQueueAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<IReadOnlyCollection<CompanyProfile>>(cts.Token));

        var act = () => CreateSut().Handle(
            new ListValidationQueueQuery { Page = 0, PageSize = 20 }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
