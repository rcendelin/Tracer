using Tracer.Domain.Interfaces;

namespace Tracer.Infrastructure.Providers.AiExtractor;

/// <summary>
/// No-op implementation of <see cref="IAiExtractorClient"/> used when Azure OpenAI is not configured.
/// Always returns <see langword="null"/>. The provider skips enrichment gracefully.
/// </summary>
internal sealed class NullAiExtractorClient : IAiExtractorClient
{
    public Task<AiExtractedData?> ExtractCompanyInfoAsync(
        string textContent,
        TraceContext context,
        CancellationToken ct) =>
        Task.FromResult<AiExtractedData?>(null);
}
