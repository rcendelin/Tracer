import {
  createContext,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import type { Toast, ToastApi, ToastInput, ToastKind } from './types';

const ToastContext = createContext<ToastApi | null>(null);

export { ToastContext };

const MAX_TOASTS = 5;
const DEFAULT_DURATION_MS = 5_000;

function kindClasses(kind: ToastKind): { border: string; icon: string; label: string } {
  switch (kind) {
    case 'success':
      return { border: 'border-green-300 bg-green-50', icon: '✅', label: 'Success' };
    case 'warning':
      return { border: 'border-yellow-300 bg-yellow-50', icon: '⚠️', label: 'Warning' };
    case 'error':
      return { border: 'border-red-300 bg-red-50', icon: '⛔', label: 'Error' };
    case 'info':
    default:
      return { border: 'border-blue-300 bg-blue-50', icon: 'ℹ️', label: 'Info' };
  }
}

function isAssertive(kind: ToastKind): boolean {
  // Error + warning interrupt. Info + success announce politely without
  // breaking the user's current reading position.
  return kind === 'error' || kind === 'warning';
}

/**
 * Toast context provider. Renders two ARIA live regions (polite + assertive)
 * so screen readers announce toasts appropriately.
 *
 * - Queue is bounded at {@link MAX_TOASTS}; the oldest toast is dropped
 *   when the limit is reached.
 * - Each toast auto-dismisses after `durationMs` (default 5s). A duration
 *   of 0 makes it sticky.
 * - Safe for StrictMode: timeouts are tracked in a ref and cleared on
 *   unmount and on manual dismiss.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const timersRef = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map());

  const clearTimer = useCallback((id: string) => {
    const handle = timersRef.current.get(id);
    if (handle !== undefined) {
      clearTimeout(handle);
      timersRef.current.delete(id);
    }
  }, []);

  const dismiss = useCallback(
    (id: string) => {
      clearTimer(id);
      setToasts((prev) => prev.filter((t) => t.id !== id));
    },
    [clearTimer],
  );

  const dismissAll = useCallback(() => {
    timersRef.current.forEach(clearTimeout);
    timersRef.current.clear();
    setToasts([]);
  }, []);

  const push = useCallback(
    (input: ToastInput): string => {
      const id =
        typeof crypto !== 'undefined' && 'randomUUID' in crypto
          ? crypto.randomUUID()
          : `t_${Date.now()}_${Math.random().toString(36).slice(2, 8)}`;

      const toast: Toast = { id, ...input };

      setToasts((prev) => {
        const next = [...prev, toast];
        return next.length > MAX_TOASTS ? next.slice(next.length - MAX_TOASTS) : next;
      });

      const duration = toast.durationMs ?? DEFAULT_DURATION_MS;
      if (duration > 0) {
        const handle = setTimeout(() => dismiss(id), duration);
        timersRef.current.set(id, handle);
      }

      return id;
    },
    [dismiss],
  );

  // Clear all pending timers on unmount to avoid leaking during HMR.
  useEffect(() => {
    const timers = timersRef.current;
    return () => {
      timers.forEach(clearTimeout);
      timers.clear();
    };
  }, []);

  const api = useMemo<ToastApi>(() => ({ push, dismiss, dismissAll }), [push, dismiss, dismissAll]);

  const politeToasts = toasts.filter((t) => !isAssertive(t.kind));
  const assertiveToasts = toasts.filter((t) => isAssertive(t.kind));

  return (
    <ToastContext.Provider value={api}>
      {children}
      <ToastStack toasts={politeToasts} live="polite" onDismiss={dismiss} />
      <ToastStack toasts={assertiveToasts} live="assertive" onDismiss={dismiss} />
    </ToastContext.Provider>
  );
}

function ToastStack({
  toasts,
  live,
  onDismiss,
}: {
  toasts: Toast[];
  live: 'polite' | 'assertive';
  onDismiss: (id: string) => void;
}) {
  return (
    <div
      role={live === 'assertive' ? 'alert' : 'status'}
      aria-live={live}
      aria-atomic="false"
      className="fixed bottom-4 right-4 z-50 flex flex-col gap-2 w-[calc(100%-2rem)] sm:w-96 pointer-events-none"
    >
      {toasts.map((toast) => {
        const { border, icon, label } = kindClasses(toast.kind);
        return (
          <div
            key={toast.id}
            className={`pointer-events-auto rounded-lg border shadow-lg p-3 flex items-start gap-2 bg-white ${border}`}
          >
            <span className="text-xl leading-none shrink-0" aria-hidden="true">
              {icon}
            </span>
            <div className="flex-1 min-w-0">
              <p className="text-sm font-semibold text-gray-900">
                <span className="sr-only">{label}: </span>
                {toast.title}
              </p>
              {toast.description && (
                <p className="text-xs text-gray-600 mt-0.5 break-words">
                  {toast.description}
                </p>
              )}
            </div>
            <button
              type="button"
              onClick={() => onDismiss(toast.id)}
              aria-label={`Dismiss ${label.toLowerCase()} notification`}
              className="shrink-0 text-gray-400 hover:text-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-500 rounded"
            >
              <span aria-hidden="true">✕</span>
            </button>
          </div>
        );
      })}
    </div>
  );
}
