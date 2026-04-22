import { useQuery } from '@tanstack/react-query';
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { analyticsApi } from '../api/client';

const SEVERITY_COLORS = {
  critical: '#dc2626', // red-600
  major: '#f59e0b',    // amber-500
  minor: '#2563eb',    // blue-600
  cosmetic: '#9ca3af', // gray-400
} as const;

interface Props {
  months?: number;
}

/**
 * Line chart of monthly change-event counts broken down by severity.
 * Data backend: GET /api/analytics/changes (aggregate-only, no PII).
 */
export function ChangesTrendChart({ months = 12 }: Props) {
  const { data, isLoading, isError } = useQuery({
    queryKey: ['analytics-changes', { months }],
    queryFn: () => analyticsApi.changes({ period: 'Monthly', months }),
    staleTime: 5 * 60_000,
  });

  const chartData = data?.buckets.map((b) => ({
    name: formatMonthLabel(b.periodStart),
    critical: b.critical,
    major: b.major,
    minor: b.minor,
    cosmetic: b.cosmetic,
  })) ?? [];

  const isEmpty = !isLoading && !isError && chartData.every((d) =>
    d.critical + d.major + d.minor + d.cosmetic === 0,
  );

  return (
    <div className="bg-white rounded-lg shadow overflow-hidden">
      <div className="px-4 py-3 bg-gray-50 border-b flex items-center justify-between">
        <h3 className="text-lg font-semibold text-gray-900">Change events — last {months} months</h3>
        <span className="text-xs text-gray-500">Aggregate, severity breakdown</span>
      </div>
      <div className="p-4 h-72">
        {isLoading && <div className="flex h-full items-center justify-center text-gray-500 text-sm">Loading…</div>}
        {isError && <div className="flex h-full items-center justify-center text-red-600 text-sm">Failed to load analytics.</div>}
        {!isLoading && !isError && isEmpty && (
          <div className="flex h-full items-center justify-center text-gray-500 text-sm">No change events in this window.</div>
        )}
        {!isLoading && !isError && !isEmpty && (
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={chartData} margin={{ top: 8, right: 16, bottom: 8, left: 0 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" />
              <XAxis dataKey="name" tick={{ fontSize: 12 }} />
              <YAxis allowDecimals={false} tick={{ fontSize: 12 }} />
              <Tooltip />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Line type="monotone" dataKey="critical" name="Critical" stroke={SEVERITY_COLORS.critical} strokeWidth={2} dot={false} />
              <Line type="monotone" dataKey="major"    name="Major"    stroke={SEVERITY_COLORS.major}    strokeWidth={2} dot={false} />
              <Line type="monotone" dataKey="minor"    name="Minor"    stroke={SEVERITY_COLORS.minor}    strokeWidth={2} dot={false} />
              <Line type="monotone" dataKey="cosmetic" name="Cosmetic" stroke={SEVERITY_COLORS.cosmetic} strokeWidth={2} dot={false} />
            </LineChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  );
}

function formatMonthLabel(iso: string) {
  // YYYY-MM-DD from the backend — slice rather than parse so the label reflects the
  // bucket's calendar month regardless of the viewer's local timezone.
  const [year, month] = iso.split('-');
  const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
  const m = Number(month);
  return `${monthNames[m - 1] ?? month} ${year.slice(2)}`;
}
