import { useEffect } from 'react';
import { useToast } from '../components/toast/useToast';
import type { UseSignalRReturn } from './useSignalR';

/**
 * Subscribes to SignalR events and surfaces them as global toasts.
 *
 * Mounted once from `Layout`. Shows:
 * - `TraceCompleted` → success toast (truncated at 80 chars on name).
 * - `ChangeDetected` with `Critical` severity → error toast (assertive).
 * - `ChangeDetected` with `Major` severity → warning toast (assertive).
 *
 * `Minor` and `Cosmetic` changes are intentionally silent — they would
 * otherwise swamp the user on busy re-validation runs. Users that want to
 * see them can open the Change Feed page.
 *
 * IMPORTANT: call this hook exactly once per app (inside `Layout`). Multiple
 * calls would each register their own SignalR handlers and duplicate toasts.
 */
export function useGlobalToasts({
  onTraceCompleted,
  onChangeDetected,
}: Pick<UseSignalRReturn, 'onTraceCompleted' | 'onChangeDetected'>) {
  const toast = useToast();

  useEffect(() => {
    return onTraceCompleted((event) => {
      toast.push({
        kind: 'success',
        title: 'Trace completed',
        description:
          event.status === 'Completed'
            ? 'Enrichment finished successfully.'
            : `Finished with status ${event.status}.`,
      });
    });
  }, [onTraceCompleted, toast]);

  useEffect(() => {
    return onChangeDetected((event) => {
      if (event.severity === 'Critical') {
        toast.push({
          kind: 'error',
          title: `Critical change — ${event.field}`,
          description: 'Review the Change Feed for details.',
        });
      } else if (event.severity === 'Major') {
        toast.push({
          kind: 'warning',
          title: `Major change — ${event.field}`,
        });
      }
      // Minor / Cosmetic: intentionally silent.
    });
  }, [onChangeDetected, toast]);
}
