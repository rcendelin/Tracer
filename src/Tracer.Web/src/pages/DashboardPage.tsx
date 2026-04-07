import { useEffect } from 'react';
import { Link } from 'react-router';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { statsApi, traceApi } from '../api/client';
import { useSignalR } from '../hooks/useSignalR';
import { StatusBadge } from '../components/StatusBadge';
import { ConfidenceBar } from '../components/ConfidenceBar';

export function DashboardPage() {
  const queryClient = useQueryClient();

  const { data: stats } = useQuery({
    queryKey: ['dashboard-stats'],
    queryFn: () => statsApi.dashboard(),
    refetchInterval: 30_000,
  });

  const { data: recentTraces } = useQuery({
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
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-gray-900">Dashboard</h2>
          <p className="text-gray-500 text-sm mt-1">Tracer Company Enrichment Engine</p>
        </div>
        <Link
          to="/trace/new"
          className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors"
        >
          + New Trace
        </Link>
      </div>

      {/* Stats cards */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
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

      {/* Quick links */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        <Link to="/trace/new" className="bg-white rounded-lg shadow p-4 hover:shadow-md transition-shadow flex items-center gap-3">
          <span className="text-2xl">➕</span>
          <div>
            <p className="font-medium text-gray-900">New Trace</p>
            <p className="text-sm text-gray-500">Submit enrichment request</p>
          </div>
        </Link>
        <Link to="/profiles" className="bg-white rounded-lg shadow p-4 hover:shadow-md transition-shadow flex items-center gap-3">
          <span className="text-2xl">🏢</span>
          <div>
            <p className="font-medium text-gray-900">Company Profiles</p>
            <p className="text-sm text-gray-500">Browse CKB directory</p>
          </div>
        </Link>
        <Link to="/traces" className="bg-white rounded-lg shadow p-4 hover:shadow-md transition-shadow flex items-center gap-3">
          <span className="text-2xl">🔍</span>
          <div>
            <p className="font-medium text-gray-900">All Traces</p>
            <p className="text-sm text-gray-500">View enrichment history</p>
          </div>
        </Link>
      </div>

      {/* Recent traces — live feed updated via SignalR */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b flex items-center justify-between">
          <h3 className="text-lg font-semibold text-gray-900">Recent Traces</h3>
          <div className="flex items-center gap-3">
            <span className="text-xs text-gray-500 flex items-center gap-1">
              <span className="h-1.5 w-1.5 rounded-full bg-green-400" />
              Live feed
            </span>
            <Link to="/traces" className="text-sm text-blue-600 hover:text-blue-800">View all</Link>
          </div>
        </div>
        <table className="w-full text-sm">
          <thead className="bg-gray-50 border-b">
            <tr>
              <th className="text-left px-4 py-2 font-medium text-gray-500">Time</th>
              <th className="text-left px-4 py-2 font-medium text-gray-500">Company</th>
              <th className="text-left px-4 py-2 font-medium text-gray-500">Status</th>
              <th className="text-left px-4 py-2 font-medium text-gray-500">Confidence</th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {(!recentTraces || recentTraces.items.length === 0) && (
              <tr>
                <td colSpan={4} className="text-center py-6 text-gray-500">
                  No traces yet. <Link to="/trace/new" className="text-blue-600 hover:underline">Submit your first trace</Link>
                </td>
              </tr>
            )}
            {recentTraces?.items.map((trace) => (
              <tr key={trace.traceId}>
                <td className="px-4 py-2 text-gray-600">{formatDate(trace.createdAt)}</td>
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
    </div>
  );
}
