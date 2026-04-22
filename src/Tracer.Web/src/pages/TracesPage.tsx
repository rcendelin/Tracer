import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import { useTraces } from '../hooks/useTraces';
import { useSignalR } from '../hooks/useSignalR';
import { StatusBadge } from '../components/StatusBadge';
import { ConfidenceBar } from '../components/ConfidenceBar';
import { Pagination } from '../components/Pagination';
import { SkeletonTable } from '../components/skeleton/Skeleton';
import { ErrorMessage } from '../components/ErrorMessage';
import { EmptyState } from '../components/EmptyState';
import type { TraceStatus } from '../types';

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Pending', label: 'Pending' },
  { value: 'InProgress', label: 'In Progress' },
  { value: 'Completed', label: 'Completed' },
  { value: 'PartiallyCompleted', label: 'Partial' },
  { value: 'Failed', label: 'Failed' },
  { value: 'Cancelled', label: 'Cancelled' },
  { value: 'Queued', label: 'Queued' },
];

export function TracesPage() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(0);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  // Per-row live status overrides pushed from SignalR. Key = traceId.
  const [liveStatuses, setLiveStatuses] = useState<Record<string, TraceStatus>>({});

  const { data, isLoading, isError, error, refetch } = useTraces({
    page,
    pageSize: 20,
    status: (statusFilter || undefined) as TraceStatus | undefined,
    search: search || undefined,
  });

  const { onTraceCompleted } = useSignalR();

  // When a trace completes, update its row live and then refresh the list
  // so pagination counts / ordering stay accurate.
  useEffect(() => {
    const timers: ReturnType<typeof setTimeout>[] = [];

    const unsub = onTraceCompleted((event) => {
      setLiveStatuses((prev) => ({ ...prev, [event.traceId]: event.status }));
      timers.push(
        setTimeout(() => {
          void queryClient.invalidateQueries({ queryKey: ['traces'] });
        }, 1_500),
      );
    });

    return () => {
      unsub();
      timers.forEach(clearTimeout);
    };
  }, [onTraceCompleted, queryClient]);

  // Clear live status overrides when filters or page change so stale entries
  // from a previous page/filter combination don't bleed into the new view.
  useEffect(() => {
    setLiveStatuses({});
  }, [page, search, statusFilter]);

  const formatDate = (dateStr: string) => {
    const d = new Date(dateStr);
    return d.toLocaleString('cs-CZ', { dateStyle: 'short', timeStyle: 'short' });
  };

  const hasFilters = Boolean(search || statusFilter);

  return (
    <div>
      <div className="flex items-center justify-between mb-6 gap-3 flex-wrap">
        <h2 className="text-2xl font-bold text-gray-900">Trace Requests</h2>
        <button
          type="button"
          onClick={() => navigate('/trace/new')}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          + New Trace
        </button>
      </div>

      {/* Filters */}
      <div className="flex gap-3 mb-4 flex-col sm:flex-row">
        <label className="flex-1">
          <span className="sr-only">Search traces</span>
          <input
            type="text"
            placeholder="Search company name or registration ID..."
            value={search}
            onChange={(e) => { setSearch(e.target.value); setPage(0); }}
            className="w-full px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
        </label>
        <label>
          <span className="sr-only">Filter by status</span>
          <select
            value={statusFilter}
            onChange={(e) => { setStatusFilter(e.target.value); setPage(0); }}
            className="w-full sm:w-auto px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
          >
            {STATUS_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
        </label>
      </div>

      {isError ? (
        <ErrorMessage
          title="Could not load traces"
          error={error}
          onRetry={() => void refetch()}
        />
      ) : isLoading ? (
        <SkeletonTable rows={6} columns={['w-24', 'w-1/3', 'w-20', 'w-24', 'w-20']} />
      ) : !data || data.items.length === 0 ? (
        <div className="bg-white rounded-lg shadow">
          <EmptyState
            icon="🔍"
            title={hasFilters ? 'No traces match your filters' : 'No traces yet'}
            description={
              hasFilters
                ? 'Try clearing or adjusting the search and status filter.'
                : 'Submit your first enrichment request to see results here.'
            }
            action={
              hasFilters
                ? { label: 'Clear filters', onClick: () => { setSearch(''); setStatusFilter(''); setPage(0); } }
                : { label: '+ New Trace', to: '/trace/new' }
            }
          />
        </div>
      ) : (
        <>
          <div className="bg-white rounded-lg shadow overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead className="bg-gray-50 border-b">
                  <tr>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Timestamp</th>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Company</th>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Status</th>
                    <th scope="col" className="text-left px-4 py-3 font-medium text-gray-500">Confidence</th>
                    <th scope="col" className="text-right px-4 py-3 font-medium text-gray-500">Duration</th>
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {data.items.map((trace) => {
                    const displayStatus = liveStatuses[trace.traceId] ?? trace.status;
                    const isLive = trace.traceId in liveStatuses && liveStatuses[trace.traceId] !== trace.status;
                    return (
                      <tr
                        key={trace.traceId}
                        onClick={() => navigate(`/traces/${trace.traceId}`)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' || e.key === ' ') {
                            e.preventDefault();
                            navigate(`/traces/${trace.traceId}`);
                          }
                        }}
                        tabIndex={0}
                        role="button"
                        aria-label={`Open trace ${trace.company?.legalName?.value ?? trace.traceId}`}
                        className="hover:bg-gray-50 cursor-pointer transition-colors focus:outline-none focus:bg-blue-50"
                      >
                        <td className="px-4 py-3 text-gray-600 whitespace-nowrap">{formatDate(trace.createdAt)}</td>
                        <td className="px-4 py-3 font-medium text-gray-900">
                          {trace.company?.legalName?.value ?? '-'}
                        </td>
                        <td className="px-4 py-3">
                          <div className="flex items-center gap-1.5">
                            <StatusBadge status={displayStatus} />
                            {isLive && (
                              <span className="h-1.5 w-1.5 rounded-full bg-blue-500 animate-pulse" title="Updated live" aria-label="Updated live" />
                            )}
                          </div>
                        </td>
                        <td className="px-4 py-3"><ConfidenceBar value={trace.overallConfidence} /></td>
                        <td className="px-4 py-3 text-right text-gray-600 whitespace-nowrap">
                          {trace.durationMs ? `${trace.durationMs}ms` : '-'}
                        </td>
                      </tr>
                    );
                  })}
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
