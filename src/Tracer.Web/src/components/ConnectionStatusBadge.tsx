import type { SignalRConnectionState } from '../hooks/useSignalR';

interface Props {
  state: SignalRConnectionState;
}

const config: Record<
  SignalRConnectionState,
  { dot: string; label: string; title: string }
> = {
  connected: {
    dot: 'bg-green-400',
    label: 'Live',
    title: 'Real-time updates connected',
  },
  connecting: {
    dot: 'bg-yellow-400 animate-pulse',
    label: 'Connecting',
    title: 'Establishing real-time connection…',
  },
  reconnecting: {
    dot: 'bg-yellow-400 animate-pulse',
    label: 'Reconnecting',
    title: 'Connection lost, reconnecting…',
  },
  disconnected: {
    dot: 'bg-red-400',
    label: 'Offline',
    title: 'Real-time updates unavailable',
  },
};

/**
 * Small status pill shown in the navigation bar.
 * Green = SignalR connected, Yellow (pulsing) = connecting/reconnecting, Red = disconnected.
 */
export function ConnectionStatusBadge({ state }: Props) {
  const { dot, label, title } = config[state];

  return (
    <span
      title={title}
      className="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium text-gray-600 bg-gray-100 border border-gray-200"
    >
      <span className={`h-2 w-2 rounded-full ${dot}`} />
      {label}
    </span>
  );
}
