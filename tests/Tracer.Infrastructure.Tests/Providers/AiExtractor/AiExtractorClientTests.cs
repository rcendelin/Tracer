using Azure;
using Azure.AI.OpenAI;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using OpenAI.Chat;
using Tracer.Domain.Entities;
using Tracer.Domain.Enums;
using Tracer.Domain.Interfaces;
using Tracer.Infrastructure.Providers.AiExtractor;

namespace Tracer.Infrastructure.Tests.Providers.AiExtractor;

/// <summary>
/// Unit tests for <see cref="AiExtractorClient"/>.
/// Tests inject a stub via the <c>SendAsync</c> property so no real Azure calls are made.
/// <see cref="AzureOpenAIClient"/> is constructed with a fake endpoint — it does not make
/// network calls in the constructor, so the underlying <see cref="ChatClient"/> is never invoked.
/// </summary>
public sealed class AiExtractorClientTests
{
    private static readonly AzureOpenAIClient FakeAzureClient =
        new(new Uri("https://fake.openai.azure.com"), new AzureKeyCredential("fake-key"));

    private static AiExtractorClient CreateSut(
        Func<IReadOnlyList<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<string>>? sendAsync = null) =>
        new(FakeAzureClient, "gpt-4o-mini", 2000, NullLogger<AiExtractorClient>.Instance)
        {
            SendAsync = sendAsync ?? StubReturning(ValidFullJson()),
        };

    private static TraceContext CreateContext(string companyName = "Acme Corp", string country = "CZ") =>
        new()
        {
            Request = new TraceRequest(
                companyName: companyName,
                phone: null, email: null, website: null, address: null,
                city: null, country: country,
                registrationId: null, taxId: null, industryHint: null,
                depth: TraceDepth.Deep,
                callbackUrl: null,
                source: "test"),
        };

    private static Func<IReadOnlyList<ChatMessage>, ChatCompletionOptions, CancellationToken, Task<string>>
        StubReturning(string json) =>
        (_, _, _) => Task.FromResult(json);

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractCompanyInfoAsync_ValidStructuredJson_MapsAllFields()
    {
        var sut = CreateSut(StubReturning(ValidFullJson()));

        var result = await sut.ExtractCompanyInfoAsync("Some company text", CreateContext(), default);

        result.Should().NotBeNull();
        result!.LegalName.Should().Be("Acme s.r.o.");
        result.Phone.Should().Be("+420 123 456 789");
        result.Email.Should().Be("info@acme.cz");
        result.Industry.Should().Be("Manufacturing");
        result.EmployeeRange.Should().Be("51-200");
        result.Description.Should().Be("Industrial parts manufacturer.");
    }

    [Fact]
    public async Task ExtractCompanyInfoAsync_ValidStructuredJson_MapsAddress()
    {
        var sut = CreateSut(StubReturning(ValidFullJson()));

        var result = await sut.ExtractCompanyInfoAsync("Some company text", CreateContext(), default);

        result!.Address.Should().NotBeNull();
        result.Address!.Street.Should().Be("Průmyslová 1");
        result.Address.City.Should().Be("Brno");
        result.Address.PostalCode.Should().Be("602 00");
        result.Address.Country.Should().Be("CZ");
        result.Address.Region.Should().Be("South Moravian");
    }

    [Fact]
    public async Task ExtractCompanyInfoAsync_NullableFieldsInJson_MapsNullsCorrectly()
    {
        const string json = """
            {
              "LegalName": "MiniCo",
              "Phone": null,
              "Email": null,
              "Address": null,
              "Industry": null,
              "EmployeeRange": null,
              "Description": null
            }
            """;
        var sut = CreateSut(StubReturning(json));

        var result = await sut.ExtractCompanyInfoAsync("text", CreateContext(), default);

        result.Should().NotBeNull();
        result!.LegalName.Should().Be("MiniCo");
        result.Phone.Should().BeNull();
        result.Address.Should().BeNull();
    }

    // ── Empty / no-result paths ───────────────────────────────────────────────

    [Fact]
    public async Task ExtractCompanyInfoAsync_AllNullJson_ReturnsNull()
    {
        const string json = """
            {
              "LegalName": null,
              "Phone": null,
              "Email": null,
              "Address": null,
              "Industry": null,
              "EmployeeRange": null,
              "Description": null
            }
            """;
        var sut = CreateSut(StubReturning(json));

        var result = await sut.ExtractCompanyInfoAsync("text", CreateContext(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExtractCompanyInfoAsync_MalformedJson_FallsBackToRegex()
    {
        // Truncated / non-schema JSON — but contains recognizable key-value pairs
        const string nonSchemaJson = """{"LegalName": "Partial Corp", "Phone": "+420999000111"}""";
        var sut = CreateSut(StubReturning(nonSchemaJson));

        var result = await sut.ExtractCompanyInfoAsync("text", CreateContext(), default);

        // Regex fallback should extract LegalName and Phone even without schema
        result.Should().NotBeNull();
        result!.LegalName.Should().Be("Partial Corp");
        result.Phone.Should().Be("+420999000111");
        // Address is intentionally not extracted by regex fallback
        result.Address.Should().BeNull();
    }

    [Fact]
    public async Task ExtractCompanyInfoAsync_CompletelyGarbageResponse_ReturnsNull()
    {
        var sut = CreateSut(StubReturning("Not JSON at all — model went off-script."));

        var result = await sut.ExtractCompanyInfoAsync("text", CreateContext(), default);

        result.Should().BeNull();
    }

    // ── Error handling ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractCompanyInfoAsync_RequestFailedException_ReturnsNull()
    {
        var sut = CreateSut((_, _, _) =>
            Task.FromException<string>(new RequestFailedException(429, "Rate limited")));

        var result = await sut.ExtractCompanyInfoAsync("text", CreateContext(), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExtractCompanyInfoAsync_PollyTimeout_PropagatesOperationCanceled()
    {
        // Simulate Polly timeout: OperationCanceledException with a non-cancelled token
        using var cts = new CancellationTokenSource();
        var token = cts.Token; // not cancelled by the time we throw

        var sut = CreateSut((_, _, _) =>
            Task.FromException<string>(new OperationCanceledException("Polly timeout", token)));

        Func<Task> act = () => sut.ExtractCompanyInfoAsync("text", CreateContext(), token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtractCompanyInfoAsync_CallerCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sut = CreateSut((_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("");
        });

        Func<Task> act = () => sut.ExtractCompanyInfoAsync("text", CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Input validation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExtractCompanyInfoAsync_EmptyOrWhitespaceText_ThrowsArgumentException(string input)
    {
        var sut = CreateSut();

        Func<Task> act = () => sut.ExtractCompanyInfoAsync(input, CreateContext(), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExtractCompanyInfoAsync_TextExceeds32KB_TruncatedAndStillSucceeds()
    {
        // Create a 40 KB string (well over the 32 KB limit)
        var largeText = new string('A', 40 * 1024);
        string? capturedPrompt = null;

        var sut = CreateSut((messages, _, _) =>
        {
            capturedPrompt = messages[1].Content[0].Text;
            return Task.FromResult(ValidFullJson());
        });

        var result = await sut.ExtractCompanyInfoAsync(largeText, CreateContext(), default);

        result.Should().NotBeNull();
        // The user message should be shorter than the input (truncated at 32 KB + prompt prefix)
        capturedPrompt.Should().NotBeNullOrEmpty();
        capturedPrompt!.Length.Should().BeLessThan(largeText.Length);
    }

    // ── Prompt construction ───────────────────────────────────────────────────

    [Fact]
    public async Task ExtractCompanyInfoAsync_ContextWithHints_IncludesHintsInUserPrompt()
    {
        string? capturedUserMessage = null;

        var sut = CreateSut((messages, _, _) =>
        {
            capturedUserMessage = messages[1].Content[0].Text;
            return Task.FromResult(ValidFullJson());
        });

        await sut.ExtractCompanyInfoAsync("company text", CreateContext("Acme Corp", "CZ"), default);

        capturedUserMessage.Should().Contain("Acme Corp");
        capturedUserMessage.Should().Contain("CZ");
    }

    [Fact]
    public async Task ExtractCompanyInfoAsync_SystemPromptAlwaysFirst()
    {
        IReadOnlyList<ChatMessage>? capturedMessages = null;

        var sut = CreateSut((messages, _, _) =>
        {
            capturedMessages = messages;
            return Task.FromResult(ValidFullJson());
        });

        await sut.ExtractCompanyInfoAsync("text", CreateContext(), default);

        capturedMessages.Should().HaveCount(2);
        capturedMessages![0].Should().BeOfType<SystemChatMessage>();
        capturedMessages[1].Should().BeOfType<UserChatMessage>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ValidFullJson() => """
        {
          "LegalName": "Acme s.r.o.",
          "Phone": "+420 123 456 789",
          "Email": "info@acme.cz",
          "Address": {
            "Street": "Průmyslová 1",
            "City": "Brno",
            "PostalCode": "602 00",
            "Country": "CZ",
            "Region": "South Moravian"
          },
          "Industry": "Manufacturing",
          "EmployeeRange": "51-200",
          "Description": "Industrial parts manufacturer."
        }
        """;
}
