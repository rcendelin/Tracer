import { useState } from 'react';
import { useNavigate } from 'react-router';
import { useTraces } from '../hooks/useTraces';
import { StatusBadge } from '../components/StatusBadge';
import { ConfidenceBar } from '../components/ConfidenceBar';
import { Pagination } from '../components/Pagination';
import type { TraceStatus } from '../types';

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Pending', label: 'Pending' },
  { value: 'InProgress', label: 'In Progress' },
  { value: 'Completed', label: 'Completed' },
  { value: 'PartiallyCompleted', label: 'Partial' },
  { value: 'Failed', label: 'Failed' },
  { value: 'Cancelled', label: 'Cancelled' },
];

export function TracesPage() {
  const navigate = useNavigate();
  const [page, setPage] = useState(0);
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  const { data, isLoading, isError, error } = useTraces({
    page,
    pageSize: 20,
    status: (statusFilter || undefined) as TraceStatus | undefined,
    search: search || undefined,
  });

  const formatDate = (dateStr: string) => {
    const d = new Date(dateStr);
    return d.toLocaleString('cs-CZ', { dateStyle: 'short', timeStyle: 'short' });
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <h2 className="text-2xl font-bold text-gray-900">Trace Requests</h2>
        <button
          onClick={() => navigate('/trace/new')}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors"
        >
          + New Trace
        </button>
      </div>

      {/* Filters */}
      <div className="flex gap-3 mb-4">
        <input
          type="text"
          placeholder="Search company name or registration ID..."
          value={search}
          onChange={(e) => { setSearch(e.target.value); setPage(0); }}
          className="flex-1 px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        />
        <select
          value={statusFilter}
          onChange={(e) => { setStatusFilter(e.target.value); setPage(0); }}
          className="px-3 py-2 border rounded-lg text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          {STATUS_OPTIONS.map((opt) => (
            <option key={opt.value} value={opt.value}>{opt.label}</option>
          ))}
        </select>
      </div>

      {/* Loading */}
      {isLoading && (
        <div className="text-center py-10 text-gray-500">Loading...</div>
      )}

      {/* Error */}
      {isError && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700 text-sm">
          Error loading traces: {error instanceof Error ? error.message : 'Unknown error'}
        </div>
      )}

      {/* Table */}
      {data && (
        <>
          <div className="bg-white rounded-lg shadow overflow-hidden">
            <table className="w-full text-sm">
              <thead className="bg-gray-50 border-b">
                <tr>
                  <th className="text-left px-4 py-3 font-medium text-gray-500">Timestamp</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500">Company</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500">Status</th>
                  <th className="text-left px-4 py-3 font-medium text-gray-500">Confidence</th>
                  <th className="text-right px-4 py-3 font-medium text-gray-500">Duration</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {data.items.length === 0 && (
                  <tr>
                    <td colSpan={5} className="text-center py-8 text-gray-500">
                      No traces found
                    </td>
                  </tr>
                )}
                {data.items.map((trace) => (
                  <tr
                    key={trace.traceId}
                    onClick={() => navigate(`/traces/${trace.traceId}`)}
                    className="hover:bg-gray-50 cursor-pointer transition-colors"
                  >
                    <td className="px-4 py-3 text-gray-600">{formatDate(trace.createdAt)}</td>
                    <td className="px-4 py-3 font-medium text-gray-900">
                      {trace.company?.legalName?.value ?? '-'}
                    </td>
                    <td className="px-4 py-3"><StatusBadge status={trace.status} /></td>
                    <td className="px-4 py-3"><ConfidenceBar value={trace.overallConfidence} /></td>
                    <td className="px-4 py-3 text-right text-gray-600">
                      {trace.durationMs ? `${trace.durationMs}ms` : '-'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
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
