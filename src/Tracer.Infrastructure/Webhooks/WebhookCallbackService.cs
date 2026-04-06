using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Tracer.Application.DTOs;
using Tracer.Application.Services;

namespace Tracer.Infrastructure.Webhooks;

/// <summary>
/// Sends HTTP POST webhook callbacks with trace results.
/// Configured with retry policy (3x exponential backoff) via Polly resilience handler.
/// Failures are logged and silently swallowed — the trace result is still
/// accessible via GET /api/trace/{id}.
/// </summary>
internal sealed partial class WebhookCallbackService : IWebhookCallbackService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebhookCallbackService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public WebhookCallbackService(HttpClient httpClient, ILogger<WebhookCallbackService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SendCallbackAsync(Uri callbackUrl, TraceResultDto result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callbackUrl);
        ArgumentNullException.ThrowIfNull(result);

        if (callbackUrl.Scheme != Uri.UriSchemeHttps)
        {
            LogCallbackRejected(callbackUrl.AbsoluteUri);
            return;
        }

        try
        {
            LogCallbackSending(callbackUrl.AbsoluteUri, result.TraceId);

            var response = await _httpClient.PostAsJsonAsync(
                callbackUrl, result, JsonOptions, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                LogCallbackSuccess(callbackUrl.AbsoluteUri, result.TraceId, (int)response.StatusCode);
            }
            else
            {
                LogCallbackFailed(callbackUrl.AbsoluteUri, result.TraceId, (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            LogCallbackError(ex, callbackUrl.AbsoluteUri, result.TraceId);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogCallbackTimeout(ex, callbackUrl.AbsoluteUri, result.TraceId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook: Rejected non-HTTPS callback URL {CallbackUrl}")]
    private partial void LogCallbackRejected(string callbackUrl);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Webhook: Sending callback to {CallbackUrl} for trace {TraceId}")]
    private partial void LogCallbackSending(string callbackUrl, Guid traceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook: Callback to {CallbackUrl} for trace {TraceId} succeeded (HTTP {StatusCode})")]
    private partial void LogCallbackSuccess(string callbackUrl, Guid traceId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook: Callback to {CallbackUrl} for trace {TraceId} failed (HTTP {StatusCode})")]
    private partial void LogCallbackFailed(string callbackUrl, Guid traceId, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook: Callback to {CallbackUrl} for trace {TraceId} error")]
    private partial void LogCallbackError(Exception ex, string callbackUrl, Guid traceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook: Callback to {CallbackUrl} for trace {TraceId} timed out")]
    private partial void LogCallbackTimeout(Exception ex, string callbackUrl, Guid traceId);
}
