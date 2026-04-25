using Microsoft.AspNetCore.SignalR;

namespace Tracer.Api.Hubs;

/// <summary>
/// SignalR hub for real-time trace enrichment updates.
/// Endpoint: <c>/hubs/trace</c>
/// </summary>
/// <remarks>
/// <para><strong>Server → Client events:</strong></para>
/// <list type="bullet">
///   <item><c>SourceCompleted</c> — a provider finished enrichment (sent to trace group)</item>
///   <item><c>TraceCompleted</c> — entire trace request completed (sent to trace group)</item>
///   <item><c>ChangeDetected</c> — a field change was detected on a profile (broadcast to all)</item>
/// </list>
/// <para><strong>Client → Server methods:</strong></para>
/// <list type="bullet">
///   <item><c>SubscribeToTrace(traceId)</c> — join the per-trace group to receive trace events</item>
///   <item><c>UnsubscribeFromTrace(traceId)</c> — leave the per-trace group</item>
/// </list>
/// <para><strong>Group naming:</strong> trace groups use the string representation of the
/// TraceId GUID (lowercase, with dashes), e.g. <c>"a1b2c3d4-..."</c>.</para>
/// </remarks>
internal sealed class TraceHub : Hub
{
    /// <summary>
    /// Client joins the per-trace group to receive <c>SourceCompleted</c> and
    /// <c>TraceCompleted</c> events for the specified trace request.
    /// </summary>
    /// <param name="traceId">The trace ID to subscribe to.</param>
    public Task SubscribeToTrace(string traceId)
    {
        if (!Guid.TryParse(traceId, out _))
            return Task.CompletedTask; // Silently ignore malformed IDs

        return Groups.AddToGroupAsync(Context.ConnectionId, traceId);
    }

    /// <summary>
    /// Client leaves the per-trace group.
    /// </summary>
    /// <param name="traceId">The trace ID to unsubscribe from.</param>
    public Task UnsubscribeFromTrace(string traceId)
    {
        if (!Guid.TryParse(traceId, out _))
            return Task.CompletedTask;

        return Groups.RemoveFromGroupAsync(Context.ConnectionId, traceId);
    }
}
