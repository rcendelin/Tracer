namespace Tracer.Application.Services;

/// <summary>
/// No-op implementation of <see cref="ILlmDisambiguatorClient"/> used when Azure OpenAI is
/// not configured. Always returns <see langword="null"/>, which <see cref="LlmDisambiguator"/>
/// treats as "no match" — the trace proceeds to create a new profile.
/// </summary>
internal sealed class NullLlmDisambiguatorClient : ILlmDisambiguatorClient
{
    public Task<DisambiguationResponse?> DisambiguateAsync(
        DisambiguationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult<DisambiguationResponse?>(null);
}
