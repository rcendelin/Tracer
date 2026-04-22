import { useEffect } from 'react';
import { Link } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { statsApi, traceApi } from '../api/client';
import { useSignalR } from '../hooks/useSignalR';
import { StatusBadge } from '../components/StatusBadge';
import { ConfidenceBar } from '../components/ConfidenceBar';
import { SkeletonCard, SkeletonTable } from '../components/skeleton/Skeleton';
import { ErrorMessage } from '../components/ErrorMessage';
import { EmptyState } from '../components/EmptyState';

export function DashboardPage() {
  const queryClient = useQueryClient();

  const {
    data: stats,
    isLoading: statsLoading,
    isError: statsError,
    error: statsErrorObj,
    refetch: refetchStats,
  } = useQuery({
    queryKey: ['dashboard-stats'],
    queryFn: () => statsApi.dashboard(),
    refetchInterval: 30_000,
  });

  const {
    data: recentTraces,
    isLoading: tracesLoading,
    isError: tracesError,
    error: tracesErrorObj,
    refetch: refetchTraces,
  } = useQuery({
    queryKey: ['traces', { page: 0, pageSize: 10 }],
    queryFn: () => traceApi.list({ page: 0, pageSize: 10 }),
    refetchInterval: 10_000,
  });

  const { onTraceCompleted } = useSignalR();

  // When any trace completes, refresh the recent-traces list and stats
  // so the dashboard live-feeds completed enrichments without page reload.
  useEffect(() => {
    return onTraceCompleted(() => {
      void queryClient.invalidateQueries({ queryKey: ['traces'] });
      void queryClient.invalidateQueries({ queryKey: ['dashboard-stats'] });
    });
  }, [onTraceCompleted, queryClient]);

  const formatDate = (dateStr: string) => {
    return new Date(dateStr).toLocaleString('cs-CZ', { dateStyle: 'short', timeStyle: 'short' });
  };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between gap-3 flex-wrap">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">Dashboard</h2>
          <p className="text-gray-500 text-sm mt-1">Tracer Company Enrichment Engine</p>
        </div>
        <Link
          to="/trace/new"
          className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          + New Trace
        </Link>
      </div>

      {/* Stats cards */}
      {statsError ? (
        <ErrorMessage
          title="Could not load dashboard stats"
          error={statsErrorObj}
          onRetry={() => void refetchStats()}
        />
      ) : statsLoading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-4">
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
          <SkeletonCard />
        </div>
      ) : (
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-4">
          <div className="bg-white rounded-lg shadow p-6">
            <h3 className="text-sm font-medium text-gray-500">Traces Today</h3>
            <p className="text-3xl font-bold text-gray-900 mt-2">{stats?.tracesToday ?? '-'}</p>
          </div>
          <div className="bg-white rounded-lg shadow p-6">
            <h3 className="text-sm font-medium text-gray-500">Traces This Week</h3>
            <p className="text-3xl font-bold text-gray-900 mt-2">{stats?.tracesThisWeek ?? '-'}</p>
          </div>
          <div className="bg-white rounded-lg shadow p-6">
            <h3 className="text-sm font-medium text-gray-500">CKB Profiles</h3>
            <p className="text-3xl font-bold text-gray-900 mt-2">{stats?.totalProfiles ?? '-'}</p>
          </div>
          <div className="bg-white rounded-lg shadow p-6">
            <h3 className="text-sm font-medium text-gray-500">Avg Confidence</h3>
            <p className="text-3xl font-bold text-gray-900 mt-2">
              {stats?.averageConfidence ? `${Math.round(stats.averageConfidence * 100)}%` : '-'}
            </p>
          </div>
        </div>
      )}

      {/* Quick links */}
      <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-4">
        <Link to="/trace/new" className="bg-white rounded-lg shadow p-4 hover:shadow-md transition-shadow flex items-center gap-3 focus:outline-none focus:ring-2 focus:ring-blue-500">
          <span className="text-2xl" aria-hidden="true">➕</span>
          <div>
            <p className="font-medium text-gray-900">New Trace</p>
            <p className="text-sm text-gray-500">Submit enrichment request</p>
          </div>
        </Link>
        <Link to="/profiles" className="bg-white rounded-lg shadow p-4 hover:shadow-md transition-shadow flex items-center gap-3 focus:outline-none focus:ring-2 focus:ring-blue-500">
          <span className="text-2xl" aria-hidden="true">🏢</span>
          <div>
            <p className="font-medium text-gray-900">Company Profiles</p>
            <p className="text-sm text-gray-500">Browse CKB directory</p>
          </div>
        </Link>
        <Link to="/traces" className="bg-white rounded-lg shadow p-4 hover:shadow-md transition-shadow flex items-center gap-3 focus:outline-none focus:ring-2 focus:ring-blue-500">
          <span className="text-2xl" aria-hidden="true">🔍</span>
          <div>
            <p className="font-medium text-gray-900">All Traces</p>
            <p className="text-sm text-gray-500">View enrichment history</p>
          </div>
        </Link>
      </div>

      {/* Recent traces — live feed updated via SignalR */}
      <section className="bg-white rounded-lg shadow overflow-hidden" aria-labelledby="recent-traces-heading">
        <div className="px-4 py-3 bg-gray-50 border-b flex items-center justify-between gap-2 flex-wrap">
          <h3 id="recent-traces-heading" className="text-lg font-semibold text-gray-900">
            Recent Traces
          </h3>
          <div className="flex items-center gap-3">
            <span className="text-xs text-gray-500 flex items-center gap-1">
              <span className="h-1.5 w-1.5 rounded-full bg-green-400" aria-hidden="true" />
              Live feed
            </span>
            <Link to="/traces" className="text-sm text-blue-600 hover:text-blue-800">View all</Link>
          </div>
        </div>

        {tracesError ? (
          <div className="p-4">
            <ErrorMessage
              title="Could not load recent traces"
              error={tracesErrorObj}
              onRetry={() => void refetchTraces()}
            />
          </div>
        ) : tracesLoading ? (
          <SkeletonTable rows={5} header className="shadow-none" />
        ) : !recentTraces || recentTraces.items.length === 0 ? (
          <EmptyState
            icon="🔍"
            title="No traces yet"
            description="Submit your first enrichment request to see results here."
            action={{ label: '+ New Trace', to: '/trace/new' }}
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 border-b">
                <tr>
                  <th scope="col" className="text-left px-4 py-2 font-medium text-gray-500">Time</th>
                  <th scope="col" className="text-left px-4 py-2 font-medium text-gray-500">Company</th>
                  <th scope="col" className="text-left px-4 py-2 font-medium text-gray-500">Status</th>
                  <th scope="col" className="text-left px-4 py-2 font-medium text-gray-500">Confidence</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {recentTraces.items.map((trace) => (
                  <tr key={trace.traceId}>
                    <td className="px-4 py-2 text-gray-600 whitespace-nowrap">{formatDate(trace.createdAt)}</td>
                    <td className="px-4 py-2 font-medium text-gray-900">
                      <Link to={`/traces/${trace.traceId}`} className="hover:text-blue-600">
                        {trace.company?.legalName?.value ?? '-'}
                      </Link>
                    </td>
                    <td className="px-4 py-2"><StatusBadge status={trace.status} /></td>
                    <td className="px-4 py-2"><ConfidenceBar value={trace.overallConfidence} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
