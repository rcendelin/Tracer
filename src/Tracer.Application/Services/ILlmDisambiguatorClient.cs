namespace Tracer.Application.Services;

/// <summary>
/// Abstraction for the LLM-backed disambiguation call (Azure OpenAI GPT-4o-mini).
/// Defined in Application so the orchestrating <see cref="LlmDisambiguator"/> doesn't
/// depend on Azure SDK types. The real implementation lives in Infrastructure.
/// </summary>
/// <remarks>
/// Marked <c>internal</c> with <c>InternalsVisibleTo Tracer.Infrastructure</c> so the
/// Infrastructure layer can implement it without exposing it on the Application public surface.
/// </remarks>
internal interface ILlmDisambiguatorClient
{
    /// <summary>
    /// Asks the LLM to pick the best candidate for the given query, or indicate none match.
    /// </summary>
    /// <returns>
    /// The LLM's response, or <see langword="null"/> if the service is not configured
    /// (Null client) or an error occurred (timeout, parse failure, Azure error).
    /// </returns>
    Task<DisambiguationResponse?> DisambiguateAsync(
        DisambiguationRequest request,
        CancellationToken cancellationToken);
}
