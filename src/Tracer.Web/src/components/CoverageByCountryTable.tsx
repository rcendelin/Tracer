import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { analyticsApi } from '../api/client';

const INITIAL_ROWS = 10;

/**
 * Per-country coverage aggregates: profile count, avg confidence, avg data age.
 * Data backend: GET /api/analytics/coverage (aggregate-only, no PII).
 */
export function CoverageByCountryTable() {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['analytics-coverage', { groupBy: 'Country' }],
    queryFn: () => analyticsApi.coverage({ groupBy: 'Country' }),
    staleTime: 5 * 60_000,
  });

  const [showAll, setShowAll] = useState(false);
  const rows = useMemo(() => data?.entries ?? [], [data]);
  const visible = showAll ? rows : rows.slice(0, INITIAL_ROWS);

  return (
    <div className="bg-white rounded-lg shadow overflow-hidden">
      <div className="px-4 py-3 bg-gray-50 border-b flex items-center justify-between">
        <h3 className="text-lg font-semibold text-gray-900">Coverage by country</h3>
        <span className="text-xs text-gray-500">
          {rows.length} {rows.length === 1 ? 'country' : 'countries'}
        </span>
      </div>
      {isLoading && <div className="p-4 text-sm text-gray-500">Loading…</div>}
      {isError && <div className="p-4 text-sm text-red-600">Failed to load coverage.</div>}
      {!isLoading && !isError && rows.length === 0 && (
        <div className="p-4 text-sm text-gray-500">No profiles in the CKB yet.</div>
      )}
      {!isLoading && !isError && rows.length > 0 && (
        <>
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                <th className="text-left px-4 py-2 font-medium text-gray-500">Country</th>
                <th className="text-right px-4 py-2 font-medium text-gray-500">Profiles</th>
                <th className="text-right px-4 py-2 font-medium text-gray-500">Avg confidence</th>
                <th className="text-right px-4 py-2 font-medium text-gray-500">Avg data age</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {visible.map((entry) => (
                <tr key={entry.group ?? '__unknown__'}>
                  <td className="px-4 py-2 font-medium text-gray-900">{entry.group ?? 'Unknown'}</td>
                  <td className="px-4 py-2 text-right text-gray-700">{entry.profileCount.toLocaleString()}</td>
                  <td className="px-4 py-2 text-right text-gray-700">
                    {entry.profileCount > 0 ? `${Math.round(entry.avgConfidence * 100)}%` : '—'}
                  </td>
                  <td className="px-4 py-2 text-right text-gray-700">
                    {entry.avgDataAgeDays > 0 ? `${Math.round(entry.avgDataAgeDays)} d` : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {rows.length > INITIAL_ROWS && (
            <div className="px-4 py-2 border-t bg-gray-50 text-right">
              <button
                type="button"
                onClick={() => setShowAll((v) => !v)}
                className="text-sm text-blue-600 hover:text-blue-800"
              >
                {showAll ? 'Show top 10' : `Show all ${rows.length}`}
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
