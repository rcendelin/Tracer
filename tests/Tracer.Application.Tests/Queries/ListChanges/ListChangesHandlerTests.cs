using FluentAssertions;
using NSubstitute;
using Tracer.Application.Queries.ListChanges;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;

namespace Tracer.Application.Tests.Queries.ListChanges;

public sealed class ListChangesHandlerTests
{
    private readonly IChangeEventRepository _repository = Substitute.For<IChangeEventRepository>();

    private ListChangesHandler CreateSut() => new(_repository);

    private static ChangeEvent CreateEvent(
        Guid? profileId = null,
        ChangeSeverity severity = ChangeSeverity.Minor) =>
        new(
            profileId ?? Guid.NewGuid(),
            FieldName.Phone,
            ChangeType.Updated,
            severity,
            previousValueJson: null,
            newValueJson: """{"Value": "+61 2 9999 0001"}""",
            detectedBy: "test");

    // ── Basic paging ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_DefaultQuery_ReturnsPagedResult()
    {
        var events = new[] { CreateEvent(), CreateEvent() };
        _repository.ListAsync(0, 20, null, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(events);
        _repository.CountAsync(null, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(2);

        var result = await CreateSut().Handle(new ListChangesQuery { Page = 0, PageSize = 20 }, CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Page.Should().Be(0);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_NegativePage_ClampsToZero()
    {
        _repository.ListAsync(0, 10, Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repository.CountAsync(Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await CreateSut().Handle(
            new ListChangesQuery { Page = -5, PageSize = 10 }, CancellationToken.None);

        result.Page.Should().Be(0);
        await _repository.Received(1).ListAsync(0, 10, Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PageSizeOver100_ClampedTo100()
    {
        _repository.ListAsync(0, 100, Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repository.CountAsync(Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await CreateSut().Handle(
            new ListChangesQuery { Page = 0, PageSize = 999 }, CancellationToken.None);

        result.PageSize.Should().Be(100);
        await _repository.Received(1).ListAsync(0, 100, Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PageSizeZero_ClampedToOne()
    {
        _repository.ListAsync(0, 1, Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _repository.CountAsync(Arg.Any<ChangeSeverity?>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await CreateSut().Handle(
            new ListChangesQuery { Page = 0, PageSize = 0 }, CancellationToken.None);

        result.PageSize.Should().Be(1);
    }

    // ── Filtering ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SeverityFilter_PassedToRepository()
    {
        _repository.ListAsync(0, 20, ChangeSeverity.Critical, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns([CreateEvent(severity: ChangeSeverity.Critical)]);
        _repository.CountAsync(ChangeSeverity.Critical, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await CreateSut().Handle(
            new ListChangesQuery { Page = 0, PageSize = 20, Severity = ChangeSeverity.Critical },
            CancellationToken.None);

        result.Items.Should().HaveCount(1);
        await _repository.Received(1).ListAsync(0, 20, ChangeSeverity.Critical, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ProfileIdFilter_PassedToRepository()
    {
        var profileId = Guid.NewGuid();
        _repository.ListAsync(0, 20, null, profileId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns([CreateEvent(profileId)]);
        _repository.CountAsync(null, profileId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await CreateSut().Handle(
            new ListChangesQuery { Page = 0, PageSize = 20, ProfileId = profileId },
            CancellationToken.None);

        result.Items.Should().HaveCount(1);
        await _repository.Received(1).ListAsync(0, 20, null, profileId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    // ── Pagination metadata ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_MultiplePages_ComputesHasNextPage()
    {
        _repository.ListAsync(0, 10, null, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 10).Select(_ => CreateEvent()).ToList());
        _repository.CountAsync(null, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(25);

        var result = await CreateSut().Handle(
            new ListChangesQuery { Page = 0, PageSize = 10 }, CancellationToken.None);

        result.TotalCount.Should().Be(25);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_LastPage_HasNextPageFalse()
    {
        _repository.ListAsync(2, 10, null, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 5).Select(_ => CreateEvent()).ToList());
        _repository.CountAsync(null, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(25);

        var result = await CreateSut().Handle(
            new ListChangesQuery { Page = 2, PageSize = 10 }, CancellationToken.None);

        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    // ── Null guard ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NullRequest_ThrowsArgumentNullException()
    {
        var act = () => CreateSut().Handle(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── DTO mapping ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_MapsChangeEventToDto()
    {
        var profileId = Guid.NewGuid();
        var ev = CreateEvent(profileId, ChangeSeverity.Major);
        _repository.ListAsync(0, 20, null, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns([ev]);
        _repository.CountAsync(null, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await CreateSut().Handle(
            new ListChangesQuery { Page = 0, PageSize = 20 }, CancellationToken.None);

        var dto = result.Items.First();
        dto.CompanyProfileId.Should().Be(profileId);
        dto.Severity.Should().Be(ChangeSeverity.Major);
        dto.Field.Should().Be(FieldName.Phone);
    }
}
