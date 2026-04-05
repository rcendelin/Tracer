import { useQuery } from '@tanstack/react-query';
import { traceApi } from '../api/client';

export function useTraceDetail(traceId: string | undefined) {
  return useQuery({
    queryKey: ['trace', traceId],
    queryFn: () => traceApi.get(traceId!),
    enabled: !!traceId,
    // Auto-refresh while trace is pending/in-progress
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'Pending' || status === 'InProgress' ? 3_000 : false;
    },
  });
}
