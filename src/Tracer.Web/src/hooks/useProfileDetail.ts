import { useQuery } from '@tanstack/react-query';
import { profileApi } from '../api/client';

export function useProfileDetail(profileId: string | undefined) {
  return useQuery({
    queryKey: ['profile', profileId],
    queryFn: () => profileApi.get(profileId!),
    enabled: !!profileId,
  });
}
