using Tracer.Application.DTOs;

namespace Tracer.Application.Services;

/// <summary>
/// Sends webhook callbacks to the caller-specified URL after trace completion.
/// </summary>
public interface IWebhookCallbackService
{
    /// <summary>
    /// Sends a POST request with the trace result to the specified callback URL.
    /// Failures are logged but do not propagate — results remain available via GET /api/trace/{id}.
    /// </summary>
    /// <param name="callbackUrl">The HTTPS URL to call.</param>
    /// <param name="result">The enrichment result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendCallbackAsync(Uri callbackUrl, TraceResultDto result, CancellationToken cancellationToken);
}
