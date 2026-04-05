import { useParams } from 'react-router';

export function ProfileDetailPage() {
  const { profileId } = useParams<{ profileId: string }>();

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-4">Profile Detail</h2>
      <p className="text-gray-500">Profile ID: {profileId}</p>
      <p className="text-gray-500">Profile detail view will be implemented in B-26+.</p>
    </div>
  );
}
