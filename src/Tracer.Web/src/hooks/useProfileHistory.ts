import { useQuery } from '@tanstack/react-query';
import { profileApi } from '../api/client';

export function useProfileHistory(profileId: string | undefined, page = 0, pageSize = 20) {
  return useQuery({
    queryKey: ['profile-history', profileId, page, pageSize],
    queryFn: () => profileApi.history(profileId!, page, pageSize),
    enabled: !!profileId,
  });
}
