import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { validationApi } from '../api/client';
import type { ValidationProgressEvent } from '../types';

interface UseValidationQueueParams {
  page?: number;
  pageSize?: number;
}

export function useValidationStats() {
  return useQuery({
    queryKey: ['validation-stats'],
    queryFn: () => validationApi.stats(),
    staleTime: 30_000,
  });
}

export function useValidationQueue(params: UseValidationQueueParams = {}) {
  return useQuery({
    queryKey: ['validation-queue', params],
    queryFn: () => validationApi.queue(params),
  });
}

/**
 * Manual revalidation trigger — enqueues the profile in the re-validation
 * Channel via POST /api/profiles/{id}/revalidate. The scheduler drains the
 * queue on the next tick regardless of the off-peak window.
 *
 * On success, invalidates both queue and stats caches so the UI reflects
 * the new queue depth immediately.
 */
export function useRevalidateProfile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (profileId: string) => validationApi.revalidate(profileId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['validation-stats'] });
      void queryClient.invalidateQueries({ queryKey: ['validation-queue'] });
    },
  });
}

/**
 * Invalidates validation stats + queue whenever SignalR pushes a
 * ValidationProgress event.
 *
 * Caller must pass the `onValidationProgress` subscription factory from an
 * existing `useSignalR()` consumer (typically `Layout`) so that no second
 * SignalR connection or competing lifecycle handlers are registered.
 * Mirrors the pattern in `useChangeFeedLiveUpdates`.
 */
export function useValidationLiveUpdates(
  onValidationProgress: (handler: (event: ValidationProgressEvent) => void) => () => void,
) {
  const queryClient = useQueryClient();

  useEffect(() => {
    return onValidationProgress(() => {
      void queryClient.invalidateQueries({ queryKey: ['validation-stats'] });
      void queryClient.invalidateQueries({ queryKey: ['validation-queue'] });
    });
  }, [onValidationProgress, queryClient]);
}
