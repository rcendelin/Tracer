using FluentAssertions;
using Tracer.Application.Services;
using Tracer.Domain.Entities;

namespace Tracer.Application.Tests.Services;

public sealed class NoOpRevalidationRunnerTests
{
    [Fact]
    public async Task RunAsync_AlwaysReturnsDeferred()
    {
        var sut = new NoOpRevalidationRunner();
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");

        var outcome = await sut.RunAsync(profile, CancellationToken.None);

        outcome.Should().Be(RevalidationOutcome.Deferred);
    }

    [Fact]
    public async Task RunAsync_NullProfile_Throws()
    {
        var sut = new NoOpRevalidationRunner();

        var act = async () => await sut.RunAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_CancelledToken_Throws()
    {
        var sut = new NoOpRevalidationRunner();
        var profile = new CompanyProfile("CZ:12345678", "CZ", "12345678");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await sut.RunAsync(profile, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
