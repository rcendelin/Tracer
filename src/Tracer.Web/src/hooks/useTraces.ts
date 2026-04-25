import { useQuery } from '@tanstack/react-query';
import { traceApi } from '../api/client';
import type { TraceStatus } from '../types';

interface UseTracesParams {
  page: number;
  pageSize: number;
  status?: TraceStatus;
  search?: string;
}

export function useTraces(params: UseTracesParams) {
  return useQuery({
    queryKey: ['traces', params],
    queryFn: () => traceApi.list({
      page: params.page,
      pageSize: params.pageSize,
      status: params.status,
      search: params.search,
    }),
    // Auto-refresh when there are pending traces
    refetchInterval: (query) => {
      const data = query.state.data;
      const hasPending = data?.items.some(
        (t) => t.status === 'Pending' || t.status === 'InProgress',
      );
      return hasPending ? 10_000 : false;
    },
  });
}
