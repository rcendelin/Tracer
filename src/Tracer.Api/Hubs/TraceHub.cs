using Microsoft.AspNetCore.SignalR;

namespace Tracer.Api.Hubs;

/// <summary>
/// SignalR hub for real-time trace enrichment updates.
/// Endpoint: <c>/hubs/trace</c>
/// </summary>
/// <remarks>
/// Server → Client events:
/// <list type="bullet">
///   <item><c>SourceCompleted</c> — a provider finished enrichment</item>
///   <item><c>TraceCompleted</c> — entire trace request completed</item>
///   <item><c>ChangeDetected</c> — a field change was detected on a profile</item>
/// </list>
/// </remarks>
internal sealed class TraceHub : Hub
{
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }
}
