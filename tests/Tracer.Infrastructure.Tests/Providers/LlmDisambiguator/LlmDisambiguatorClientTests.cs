using Azure;
using Azure.AI.OpenAI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using Tracer.Application.Services;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.ValueObjects;
using Tracer.Infrastructure.Providers.LlmDisambiguator;

namespace Tracer.Infrastructure.Tests.Providers.LlmDisambiguator;

/// <summary>
/// Tests for <see cref="LlmDisambiguatorClient"/> — uses the injectable <c>SendAsync</c> seam to
/// avoid real Azure calls. AzureOpenAIClient is instantiated with a fake URI; its constructor
/// does not perform network I/O.
/// </summary>
public sealed class LlmDisambiguatorClientTests
{
    private static LlmDisambiguatorClient CreateSut(
        Func<IReadOnlyList<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<string>>? sendAsync = null)
    {
        // Fake endpoint + dummy key; AzureOpenAIClient ctor does no network I/O.
        var azureClient = new AzureOpenAIClient(
            new Uri("https://example.openai.azure.com/"),
            new AzureKeyCredential("dummy-key-for-tests"));

        return new LlmDisambiguatorClient(
            azureClient,
            deploymentName: "gpt-4o-mini",
            maxOutputTokens: 500,
            logger: NullLogger<LlmDisambiguatorClient>.Instance)
        {
            SendAsync = sendAsync ?? StubReturning(ValidJson(0, 0.9, "match")),
        };
    }

    private static CompanyProfile ProfileFor(int id) =>
        new CompanyProfile($"NAME:CZ:p{id}", "CZ").Also(p =>
            p.UpdateField(FieldName.LegalName,
                new TracedField<string>
                {
                    Value = $"CANDIDATE {id}",
                    Confidence = Confidence.Create(0.9),
                    Source = "test",
                    EnrichedAt = DateTimeOffset.UtcNow,
                },
                "test"));

    private static DisambiguationRequest SampleRequest(int candidateCount = 3) =>
        new(
            QueryName: "Acme Corp",
            Country: "CZ",
            Candidates: Enumerable.Range(0, candidateCount)
                .Select(i => new FuzzyMatchCandidate(ProfileFor(i), 0.72 + i * 0.01))
                .ToArray());

    private static string ValidJson(int index, double confidence, string? reasoning) =>
        $$"""{"index": {{index}}, "confidence": {{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "reasoning": {{(reasoning is null ? "null" : $"\"{reasoning}\"")}}}""";

    private static Func<IReadOnlyList<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<string>>
        StubReturning(string response) => (_, _, _) => Task.FromResult(response);

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisambiguateAsync_ValidResponse_ReturnsParsedDto()
    {
        var sut = CreateSut(StubReturning(ValidJson(2, 0.85, "strong match")));

        var result = await sut.DisambiguateAsync(SampleRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.SelectedIndex.Should().Be(2);
        result.Confidence.Should().Be(0.85);
        result.Reasoning.Should().Be("strong match");
    }

    [Fact]
    public async Task DisambiguateAsync_NullReasoning_Parsed()
    {
        var sut = CreateSut(StubReturning(ValidJson(0, 0.7, null)));

        var result = await sut.DisambiguateAsync(SampleRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Reasoning.Should().BeNull();
    }

    [Fact]
    public async Task DisambiguateAsync_IndexNegativeOne_Parsed()
    {
        var sut = CreateSut(StubReturning(ValidJson(-1, 0.3, "none match")));

        var result = await sut.DisambiguateAsync(SampleRequest(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.SelectedIndex.Should().Be(-1);
    }

    // ── Error paths ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DisambiguateAsync_EmptyCandidates_ReturnsNullWithoutCallingLlm()
    {
        var called = false;
        var sut = CreateSut((_, _, _) =>
        {
            called = true;
            return Task.FromResult("{}");
        });

        var request = new DisambiguationRequest("Acme", "CZ", []);
        var result = await sut.DisambiguateAsync(request, CancellationToken.None);

        result.Should().BeNull();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task DisambiguateAsync_MalformedJson_ReturnsNull()
    {
        var sut = CreateSut(StubReturning("not-valid-json{"));

        var result = await sut.DisambiguateAsync(SampleRequest(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DisambiguateAsync_PollyTimeoutOperationCancelled_ReturnsNull()
    {
        // Polly cancels with a token different from the caller's; client must treat as soft failure.
        using var pollyCts = new CancellationTokenSource();
        await pollyCts.CancelAsync();

        var sut = CreateSut((_, _, _) =>
            throw new OperationCanceledException(pollyCts.Token));

        var result = await sut.DisambiguateAsync(SampleRequest(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DisambiguateAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = CreateSut((_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("{}");
        });

        var act = () => sut.DisambiguateAsync(SampleRequest(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DisambiguateAsync_RequestFailed_ReturnsNull()
    {
        var sut = CreateSut((_, _, _) =>
            throw new RequestFailedException(status: 429, message: "rate limited"));

        var result = await sut.DisambiguateAsync(SampleRequest(), CancellationToken.None);

        result.Should().BeNull();
    }

    // ── Prompt structure ────────────────────────────────────────────────────

    [Fact]
    public async Task DisambiguateAsync_BuildsUserPromptIncludingQueryAndCandidates()
    {
        string? capturedUserPrompt = null;
        var sut = CreateSut((messages, _, _) =>
        {
            // User prompt is the second message (after system prompt).
            var userMsg = messages[1] as UserChatMessage;
            capturedUserPrompt = userMsg?.Content[0].Text;
            return Task.FromResult(ValidJson(-1, 0.0, null));
        });

        await sut.DisambiguateAsync(SampleRequest(3), CancellationToken.None);

        capturedUserPrompt.Should().NotBeNull();
        capturedUserPrompt!.Should().Contain("Acme Corp");
        capturedUserPrompt.Should().Contain("CZ");
        capturedUserPrompt.Should().Contain("[0]");
        capturedUserPrompt.Should().Contain("[1]");
        capturedUserPrompt.Should().Contain("[2]");
        capturedUserPrompt.Should().Contain("CANDIDATE 0");
        capturedUserPrompt.Should().Contain("CANDIDATE 1");
        capturedUserPrompt.Should().Contain("CANDIDATE 2");
    }

    [Fact]
    public async Task DisambiguateAsync_SetsTemperatureAndMaxTokens()
    {
        ChatCompletionOptions? captured = null;
        var sut = CreateSut((_, options, _) =>
        {
            captured = options;
            return Task.FromResult(ValidJson(-1, 0.0, null));
        });

        await sut.DisambiguateAsync(SampleRequest(), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Temperature.Should().Be(0);
        captured.MaxOutputTokenCount.Should().Be(500);
    }

    // ── Input validation ────────────────────────────────────────────────────

    [Fact]
    public async Task DisambiguateAsync_NullRequest_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.DisambiguateAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

/// <summary>Small fluent helper to initialise CompanyProfile in a single expression.</summary>
internal static class TestCompanyProfileExtensions
{
    public static CompanyProfile Also(this CompanyProfile profile, Action<CompanyProfile> action)
    {
        action(profile);
        return profile;
    }
}
