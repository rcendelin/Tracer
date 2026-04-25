import { useQuery } from '@tanstack/react-query';
import { profileApi } from '../api/client';

interface UseProfilesParams {
  page: number;
  pageSize: number;
  search?: string;
  country?: string;
  minConfidence?: number;
  includeArchived?: boolean;
}

export function useProfiles(params: UseProfilesParams) {
  return useQuery({
    queryKey: ['profiles', params],
    queryFn: () => profileApi.list({
      page: params.page,
      pageSize: params.pageSize,
      search: params.search || undefined,
      country: params.country || undefined,
      minConfidence: params.minConfidence,
      includeArchived: params.includeArchived,
    }),
  });
}

/** Lightweight query for counting stale profiles (not validated in the last N days). */
export function useStaleProfileCount(olderThanDays: number) {
  return useQuery({
    queryKey: ['profiles-stale-count', olderThanDays],
    // Compute date inside queryFn so each refetch uses a fresh timestamp.
    queryFn: () => {
      const validatedBefore = new Date(
        Date.now() - olderThanDays * 24 * 60 * 60 * 1000,
      ).toISOString();
      return profileApi.list({ page: 0, pageSize: 1, validatedBefore });
    },
    select: (data) => data.totalCount,
    staleTime: 5 * 60_000, // 5 minutes — revalidation count changes slowly
  });
}
