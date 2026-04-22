import { useState } from 'react';
import { useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { useProfiles, useStaleProfileCount } from '../hooks/useProfiles';
import { ConfidenceBar } from '../components/ConfidenceBar';
import { Pagination } from '../components/Pagination';
import { SkeletonCard, SkeletonTable } from '../components/skeleton/Skeleton';
import { ErrorMessage } from '../components/ErrorMessage';
import { EmptyState } from '../components/EmptyState';
import { statsApi } from '../api/client';

function FreshnessBadge({ lastValidatedAt }: { lastValidatedAt?: string }) {
  if (!lastValidatedAt) {
    return <span className="inline-block px-2 py-0.5 rounded-full text-xs bg-gray-100 text-gray-500">Never</span>;
  }

  const daysSince = (Date.now() - new Date(lastValidatedAt).getTime()) / (1000 * 60 * 60 * 24);

  if (daysSince < 30) {
    return <span className="inline-block px-2 py-0.5 rounded-full text-xs bg-green-100 text-green-700">Fresh</span>;
  }
  if (daysSince < 90) {
    return <span className="inline-block px-2 py-0.5 rounded-full text-xs bg-yellow-100 text-yellow-700">Aging</span>;
  }
  return <span className="inline-block px-2 py-0.5 rounded-full text-xs bg-red-100 text-red-700">Stale</span>;
}

export function ProfilesPage() {
  const navigate = useNavigate();
  const [page, setPage] = useState(0);
  const [search, setSearch] = useState('');
  const [country, setCountry] = useState('');
  const [minConfidence, setMinConfidence] = useState(0);
  const [includeArchived, setIncludeArchived] = useState(false);

  const { data, isLoading, isError, error, refetch } = useProfiles({
    page,
    pageSize: 20,
    search: search || undefined,
    country: country || undefined,
    minConfidence: minConfidence > 0 ? minConfidence / 100 : undefined,
    includeArchived,
  });

  const { data: stats, isLoading: statsLoading } = useQuery({
    queryKey: ['dashboard-stats'],
    queryFn: () => statsApi.dashboard(),
    staleTime: 60_000,
  });

  const { data: staleCount } = useStaleProfileCount(90);

  const hasFilters = Boolean(search || country || minConfidence > 0 || includeArchived);

  const resetPage = () => setPage(0);

  const formatDate = (dateStr?: string) => {
    if (!dateStr) return '-';
    return new Date(dateStr).toLocaleString('cs-CZ', { dateStyle: 'short' });
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold text-gray-900">Company Profiles</h2>
        <span className="text-sm text-gray-500">CKB Directory</span>
      </div>

      {/* Stats header */}
      {statsLoading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-4">
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
        </div>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-4">
          <div className="bg-white rounded-lg shadow p-4">
            <p className="text-sm text-gray-500">Total Profiles</p>
            <p className="text-3xl font-bold text-gray-900 mt-1">{stats?.totalProfiles ?? '-'}</p>
          </div>
          <div className="bg-white rounded-lg shadow p-4">
            <p className="text-sm text-gray-500">Avg Confidence</p>
            <p className="text-3xl font-bold text-gray-900 mt-1">
              {stats?.averageConfidence ? `${Math.round(stats.averageConfidence * 100)}%` : '-'}
            </p>
          </div>
          <div className="bg-white rounded-lg shadow p-4">
            <p className="text-sm text-gray-500">Needing Revalidation</p>
            <p className="text-3xl font-bold text-orange-600 mt-1">{staleCount ?? '-'}</p>
            <p className="text-xs text-gray-400 mt-1">Not validated in &gt;90 days</p>
          </div>
        </div>
      )}

      {/* Filters */}
      <div className="bg-white rounded-lg shadow p-4">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
          <input
            type="text"
            placeholder="Search name or reg. ID..."
            value={search}
            onChange={(e) => { setSearch(e.target.value); resetPage(); }}
            className="px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <input
            type="text"
            placeholder="Country (e.g. CZ, GB)"
            value={country}
            onChange={(e) => { setCountry(e.target.value.toUpperCase().slice(0, 2)); resetPage(); }}
            className="px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 uppercase"
            maxLength={2}
          />
          <div className="flex flex-col gap-1">
            <label className="text-xs text-gray-500">
              Min confidence: <span className="font-medium text-gray-700">{minConfidence}%</span>
            </label>
            <input
              type="range"
              min={0}
              max={100}
              step={5}
              value={minConfidence}
              onChange={(e) => { setMinConfidence(Number(e.target.value)); resetPage(); }}
              className="w-full accent-blue-600"
            />
          </div>
          <label className="flex items-center gap-2 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(e) => { setIncludeArchived(e.target.checked); resetPage(); }}
              className="w-4 h-4 text-blue-600 rounded"
            />
            <span className="text-sm text-gray-700">Include archived</span>
          </label>
        </div>
      </div>

      {isError ? (
        <ErrorMessage
          title="Could not load profiles"
          error={error}
          onRetry={() => void refetch()}
        />
      ) : isLoading ? (
        <SkeletonTable rows={6} columns={['w-1/4', 'w-16', 'w-24', 'w-20', 'w-24', 'w-20', 'w-12']} />
      ) : !data || data.items.length === 0 ? (
        <div className="bg-white rounded-lg shadow">
          <EmptyState
            icon="🏢"
            title={hasFilters ? 'No profiles match your filters' : 'No profiles yet'}
            description={
              hasFilters
                ? 'Try widening the confidence range, clearing the country filter, or including archived profiles.'
                : 'The CKB is empty. Submit a trace to start building up the knowledge base.'
            }
            action={
              hasFilters
                ? {
                    label: 'Clear filters',
                    onClick: () => {
                      setSearch('');
                      setCountry('');
                      setMinConfidence(0);
                      setIncludeArchived(false);
                      resetPage();
                    },
                  }
                : { label: '+ New Trace', to: '/trace/new' }
            }
          />
        </div>
      ) : (
        <>
          <div className="bg-white rounded-lg shadow overflow-hidden">
            <div className="px-4 py-3 bg-gray-50 border-b">
              <span className="text-sm text-gray-500">
                {data.totalCount} profile{data.totalCount !== 1 ? 's' : ''} found
              </span>
            </div>
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 border-b">
                  <tr>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Legal Name</th>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Country</th>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Reg. ID</th>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Confidence</th>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Last Enriched</th>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Freshness</th>
                    <th scope="col" className="text-right px-4 py-3 font-medium text-gray-500">Traces</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {data.items.map((profile) => (
                    <tr
                      key={profile.id}
                      onClick={() => navigate(`/profiles/${profile.id}`)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          navigate(`/profiles/${profile.id}`);
                        }
                      }}
                      tabIndex={0}
                      role="button"
                      aria-label={`Open profile ${profile.enriched?.legalName?.value ?? profile.normalizedKey}`}
                      className="hover:bg-gray-50 cursor-pointer transition-colors focus:outline-none focus:bg-blue-50"
                    >
                      <td className="px-4 py-3 font-medium text-gray-900">
                        <div className="flex items-center gap-2">
                          {profile.enriched?.legalName?.value ?? (
                            <span className="text-gray-400 italic">Unknown</span>
                          )}
                          {profile.isArchived && (
                            <span className="text-xs px-1.5 py-0.5 bg-gray-100 text-gray-500 rounded">Archived</span>
                          )}
                        </div>
                      </td>
                      <td className="px-4 py-3 text-gray-600 font-mono text-xs">{profile.country}</td>
                      <td className="px-4 py-3 text-gray-600 font-mono text-xs">
                        {profile.registrationId ?? '-'}
                      </td>
                      <td className="px-4 py-3">
                        <ConfidenceBar value={profile.overallConfidence} />
                      </td>
                      <td className="px-4 py-3 text-gray-600 text-xs whitespace-nowrap">{formatDate(profile.lastEnrichedAt)}</td>
                      <td className="px-4 py-3">
                        <FreshnessBadge lastValidatedAt={profile.lastValidatedAt} />
                      </td>
                      <td className="px-4 py-3 text-right text-gray-600">{profile.traceCount}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <Pagination
            page={page}
            totalPages={data.totalPages}
            onPageChange={setPage}
          />
        </>
      )}
    </div>
  );
}
