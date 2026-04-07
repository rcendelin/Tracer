import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { useCallback, useEffect, useRef, useState } from 'react';
import type {
  ChangeDetectedEvent,
  SourceCompletedEvent,
  TraceCompletedEvent,
  ValidationProgressEvent,
} from '../types';

export type SignalRConnectionState = 'connecting' | 'connected' | 'disconnected' | 'reconnecting';

export interface UseSignalRReturn {
  connectionState: SignalRConnectionState;
  /**
   * Join the per-trace SignalR group to receive SourceCompleted and TraceCompleted
   * events for the specified trace. Returns a cleanup function that leaves the group.
   *
   * @example
   * useEffect(() => subscribeToTrace(traceId), [traceId, subscribeToTrace]);
   */
  subscribeToTrace: (traceId: string) => () => void;
  onSourceCompleted: (handler: (event: SourceCompletedEvent) => void) => () => void;
  onTraceCompleted: (handler: (event: TraceCompletedEvent) => void) => () => void;
  onChangeDetected: (handler: (event: ChangeDetectedEvent) => void) => () => void;
  onValidationProgress: (handler: (event: ValidationProgressEvent) => void) => () => void;
}

// Singleton connection — shared across all hook consumers within the same render tree.
// A module-level ref avoids creating multiple SignalR connections when several
// components mount simultaneously. The connection is torn down when the last
// consumer unmounts.
let sharedConnection: HubConnection | null = null;
let consumerCount = 0;

function getHubUrl(): string {
  // Vite exposes VITE_API_URL via import.meta.env; falls back to same-origin in prod.
  const base = (import.meta.env.VITE_API_URL as string | undefined) ?? '';
  return `${base}/hubs/trace`;
}

function getApiKey(): string {
  // VITE_API_KEY is provided at build time or via runtime injection.
  // When empty (development with auth disabled), the accessTokenFactory returns
  // an empty string which the backend pass-through middleware accepts.
  return (import.meta.env.VITE_API_KEY as string | undefined) ?? '';
}

/**
 * Returns the module-level shared HubConnection, creating it on first call.
 * The connection is NOT started here — callers must call .start() themselves.
 *
 * Authentication: the API key is passed via `accessTokenFactory` so the
 * SignalR client sends it as a Bearer token in the Authorization header for
 * long-polling and as `access_token` query string for WebSocket upgrades.
 * The backend ApiKeyAuthMiddleware must accept both paths.
 */
function getOrCreateConnection(): HubConnection {
  if (!sharedConnection) {
    sharedConnection = new HubConnectionBuilder()
      .withUrl(getHubUrl(), {
        accessTokenFactory: () => getApiKey(),
      })
      .withAutomaticReconnect({
        // Custom backoff: 0s, 2s, 5s, 10s, 30s, then 30s indefinitely
        nextRetryDelayInMilliseconds: (ctx) => {
          const delays = [0, 2_000, 5_000, 10_000, 30_000];
          return delays[ctx.previousRetryCount] ?? 30_000;
        },
      })
      .configureLogging(
        import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning,
      )
      .build();
  }
  return sharedConnection;
}

/**
 * React hook that manages a shared SignalR connection to /hubs/trace.
 *
 * - The connection is created lazily on first mount and torn down when the
 *   last consumer unmounts.
 * - Lifecycle handlers (onreconnecting etc.) are registered and de-registered
 *   per consumer to avoid accumulation across multiple mount/unmount cycles.
 * - Exposes typed `on*` subscription methods that return an unsubscribe callback.
 * - Reports live connection state for UI indicators.
 *
 * @example
 * const { connectionState, onTraceCompleted } = useSignalR();
 * useEffect(() => onTraceCompleted(event => console.log(event)), [onTraceCompleted]);
 */
export function useSignalR(): UseSignalRReturn {
  const [connectionState, setConnectionState] =
    useState<SignalRConnectionState>('connecting');
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    consumerCount++;
    const connection = getOrCreateConnection();
    connectionRef.current = connection;

    const handleReconnecting = () => setConnectionState('reconnecting');
    const handleReconnected = () => setConnectionState('connected');
    const handleClose = () => setConnectionState('disconnected');

    // Register lifecycle callbacks. Passing null to these methods removes
    // previously registered handlers, so we track and remove explicitly.
    connection.onreconnecting(handleReconnecting);
    connection.onreconnected(handleReconnected);
    connection.onclose(handleClose);

    const syncState = () => {
      switch (connection.state) {
        case HubConnectionState.Connected:
          setConnectionState('connected');
          break;
        case HubConnectionState.Reconnecting:
          setConnectionState('reconnecting');
          break;
        case HubConnectionState.Disconnected:
          setConnectionState('disconnected');
          break;
        default:
          setConnectionState('connecting');
      }
    };

    if (connection.state === HubConnectionState.Disconnected) {
      setConnectionState('connecting');
      connection
        .start()
        .then(() => setConnectionState('connected'))
        .catch(() => setConnectionState('disconnected'));
    } else {
      syncState();
    }

    return () => {
      // Unregister our lifecycle callbacks by registering no-op replacements.
      // The SignalR v10 typings do not allow null here; no-ops are equivalent
      // because the singleton means these are the only registered handlers.
      connection.onreconnecting(() => undefined);
      connection.onreconnected(() => undefined);
      connection.onclose(() => undefined);

      consumerCount--;
      if (consumerCount === 0 && sharedConnection) {
        sharedConnection.stop().catch(() => undefined);
        sharedConnection = null;
      }
    };
  }, []);

  const subscribeToTrace = useCallback((traceId: string) => {
    const connection = connectionRef.current;
    if (!connection) return () => undefined;
    void connection.invoke('SubscribeToTrace', traceId).catch(() => undefined);
    return () => {
      void connection.invoke('UnsubscribeFromTrace', traceId).catch(() => undefined);
    };
  }, []);

  const onSourceCompleted = useCallback(
    (handler: (event: SourceCompletedEvent) => void) => {
      const connection = connectionRef.current;
      if (!connection) return () => undefined;
      connection.on('SourceCompleted', handler);
      return () => connection.off('SourceCompleted', handler);
    },
    [],
  );

  const onTraceCompleted = useCallback(
    (handler: (event: TraceCompletedEvent) => void) => {
      const connection = connectionRef.current;
      if (!connection) return () => undefined;
      connection.on('TraceCompleted', handler);
      return () => connection.off('TraceCompleted', handler);
    },
    [],
  );

  const onChangeDetected = useCallback(
    (handler: (event: ChangeDetectedEvent) => void) => {
      const connection = connectionRef.current;
      if (!connection) return () => undefined;
      connection.on('ChangeDetected', handler);
      return () => connection.off('ChangeDetected', handler);
    },
    [],
  );

  const onValidationProgress = useCallback(
    (handler: (event: ValidationProgressEvent) => void) => {
      const connection = connectionRef.current;
      if (!connection) return () => undefined;
      connection.on('ValidationProgress', handler);
      return () => connection.off('ValidationProgress', handler);
    },
    [],
  );

  return {
    connectionState,
    subscribeToTrace,
    onSourceCompleted,
    onTraceCompleted,
    onChangeDetected,
    onValidationProgress,
  };
}
