import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { changesApi } from '../api/client';
import type { ChangeSeverity, ChangeDetectedEvent } from '../types';

interface UseChangesParams {
  page?: number;
  pageSize?: number;
  severity?: ChangeSeverity;
  profileId?: string;
}

export function useChanges(params: UseChangesParams = {}) {
  return useQuery({
    queryKey: ['changes', params],
    queryFn: () => changesApi.list(params),
  });
}

export function useChangeStats() {
  return useQuery({
    queryKey: ['change-stats'],
    queryFn: () => changesApi.stats(),
    staleTime: 30_000,
  });
}

/**
 * Invalidates the changes and change-stats query caches whenever SignalR
 * reports a ChangeDetected event.
 *
 * The `onChangeDetected` callback factory must be provided by the caller —
 * typically the component that already holds a `useSignalR()` reference
 * (e.g. `Layout`) — so that no additional SignalR consumer is created.
 * This avoids lifecycle-handler conflicts caused by having two `useSignalR()`
 * consumers competing to register/deregister the same singleton connection's
 * `onreconnecting` / `onclose` callbacks.
 */
export function useChangeFeedLiveUpdates(
  onChangeDetected: (handler: (event: ChangeDetectedEvent) => void) => () => void,
) {
  const queryClient = useQueryClient();

  useEffect(() => {
    return onChangeDetected(() => {
      void queryClient.invalidateQueries({ queryKey: ['changes'] });
      void queryClient.invalidateQueries({ queryKey: ['change-stats'] });
    });
  }, [onChangeDetected, queryClient]);
}
