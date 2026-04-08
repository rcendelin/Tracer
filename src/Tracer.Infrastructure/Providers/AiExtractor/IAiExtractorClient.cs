using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Providers.AiExtractor;

/// <summary>
/// Extracts structured company information from unstructured text using Azure OpenAI.
/// Used by <c>AiExtractorProvider</c> (B-57) as the AI inference layer.
/// </summary>
internal interface IAiExtractorClient
{
    /// <summary>
    /// Sends <paramref name="textContent"/> to the Azure OpenAI model and returns structured
    /// company data extracted from the text.
    /// </summary>
    /// <param name="textContent">
    /// Unstructured text describing a company (e.g. scraped HTML body, PDF extract).
    /// Must not be empty. Truncated to 32 KB before sending to the model.
    /// </param>
    /// <param name="context">The current trace context providing company name hints for the prompt.</param>
    /// <param name="ct">Cancellation token; also used to distinguish caller cancellation vs. Polly timeout.</param>
    /// <returns>
    /// Structured <see cref="AiExtractedData"/> if the model returned a parseable result,
    /// or <see langword="null"/> if extraction yielded no usable fields.
    /// </returns>
    Task<AiExtractedData?> ExtractCompanyInfoAsync(string textContent, TraceContext context, CancellationToken ct);
}
