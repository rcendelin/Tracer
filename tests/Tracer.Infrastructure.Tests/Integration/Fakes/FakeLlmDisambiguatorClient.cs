using Tracer.Application.Services;

namespace Tracer.Infrastructure.Tests.Integration.Fakes;

/// <summary>
/// Test stub for <see cref="ILlmDisambiguatorClient"/>. Lets E2E tests force a specific
/// candidate selection (or explicit no-match) without hitting Azure OpenAI.
/// </summary>
internal sealed class FakeLlmDisambiguatorClient : ILlmDisambiguatorClient
{
    /// <summary>
    /// Zero-based index of the candidate to return, or <c>-1</c> for "no match".
    /// Mutable so the owning test host can configure it after construction.
    /// </summary>
    public int SelectedIndex { get; set; } = -1;

    /// <summary>
    /// Raw confidence returned to the caller. <see cref="LlmDisambiguator"/> applies a ×0.7
    /// discount and a 0.5 acceptance threshold, so values &lt; ~0.72 will be rejected.
    /// </summary>
    public double Confidence { get; set; } = 0.9;

    /// <summary>Count of DisambiguateAsync calls — useful for asserting the LLM fallback fired.</summary>
    public int CallCount { get; private set; }

    /// <summary>Last request observed by this client (null until first call).</summary>
    public DisambiguationRequest? LastRequest { get; private set; }

    public Task<DisambiguationResponse?> DisambiguateAsync(
        DisambiguationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CallCount++;
        LastRequest = request;
        return Task.FromResult<DisambiguationResponse?>(
            new DisambiguationResponse(SelectedIndex, Confidence, Reasoning: "fake-disambiguator"));
    }
}
