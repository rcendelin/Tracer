import { useState } from 'react';
import { Link } from 'react-router';
import { Pagination } from '../components/Pagination';
import {
  useRevalidateProfile,
  useValidationQueue,
  useValidationStats,
} from '../hooks/useValidation';
import type { ValidationQueueItem } from '../types';

// ─── Helpers ────────────────────────────────────────────────────────────────

function relativeTime(isoString?: string): string {
  if (!isoString) return 'never';
  const diffMs = Date.now() - new Date(isoString).getTime();
  const diffMin = Math.floor(diffMs / 60_000);
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffH = Math.floor(diffMin / 60);
  if (diffH < 24) return `${diffH}h ago`;
  const diffD = Math.floor(diffH / 24);
  if (diffD < 30) return `${diffD}d ago`;
  const diffMo = Math.floor(diffD / 30);
  return `${diffMo}mo ago`;
}

function dueTime(isoString?: string): { label: string; tone: 'overdue' | 'soon' | 'later' } {
  if (!isoString) return { label: 'unknown', tone: 'later' };
  const diffMs = new Date(isoString).getTime() - Date.now();
  if (diffMs < 0) {
    const daysPast = Math.floor(-diffMs / (24 * 60 * 60_000));
    return { label: daysPast === 0 ? 'overdue' : `overdue ${daysPast}d`, tone: 'overdue' };
  }
  const diffDays = Math.floor(diffMs / (24 * 60 * 60_000));
  if (diffDays < 7) return { label: `in ${diffDays || 1}d`, tone: 'soon' };
  return { label: `in ${diffDays}d`, tone: 'later' };
}

// ─── Stat card ───────────────────────────────────────────────────────────────

function StatCard({
  label,
  value,
  suffix,
  color,
}: {
  label: string;
  value: number;
  suffix?: string;
  color: string;
}) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-4 flex flex-col gap-1">
      <span className="text-xs text-gray-500">{label}</span>
      <span className={`text-2xl font-bold ${color}`}>
        {value.toLocaleString(undefined, { maximumFractionDigits: 2 })}
        {suffix && <span className="ml-1 text-sm font-medium text-gray-400">{suffix}</span>}
      </span>
    </div>
  );
}

// ─── Queue row ───────────────────────────────────────────────────────────────

function ExpiredFieldPill({ field }: { field: string }) {
  return (
    <span className="inline-block px-2 py-0.5 rounded-full text-xs bg-orange-50 text-orange-700 ring-1 ring-orange-200">
      {field}
    </span>
  );
}

function QueueRow({
  item,
  onRevalidate,
  isPending,
}: {
  item: ValidationQueueItem;
  onRevalidate: (profileId: string) => void;
  isPending: boolean;
}) {
  const due = dueTime(item.nextFieldExpiryDate);
  const dueToneClass =
    due.tone === 'overdue'
      ? 'text-red-600 font-medium'
      : due.tone === 'soon'
        ? 'text-orange-600'
        : 'text-gray-500';

  return (
    <div className="border border-gray-200 rounded-lg p-4 hover:border-gray-300 transition-colors">
      <div className="flex items-start gap-3">
        <div className="flex flex-col gap-1 min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <Link
              to={`/profiles/${item.profileId}`}
              className="text-sm font-medium text-blue-700 hover:text-blue-900 truncate"
              title={item.legalName ?? item.normalizedKey}
            >
              {item.legalName ?? item.normalizedKey}
            </Link>
            <span className="text-xs text-gray-400 font-mono">{item.country}</span>
            {item.registrationId && (
              <span className="text-xs text-gray-500 font-mono">{item.registrationId}</span>
            )}
          </div>
          <div className="flex items-center gap-2 text-xs text-gray-500 flex-wrap">
            <span>traces: <span className="font-medium text-gray-700">{item.traceCount}</span></span>
            <span>·</span>
            <span>
              confidence:{' '}
              <span className="font-medium text-gray-700">
                {item.overallConfidence != null
                  ? `${(item.overallConfidence * 100).toFixed(0)}%`
                  : '—'}
              </span>
            </span>
            <span>·</span>
            <span>
              validated:{' '}
              <span className="font-medium text-gray-700">
                {relativeTime(item.lastValidatedAt)}
              </span>
            </span>
            <span>·</span>
            <span className={dueToneClass}>next expiry: {due.label}</span>
          </div>
          <div className="flex items-center gap-1 mt-1 flex-wrap">
            {item.expiredFields.map((f) => (
              <ExpiredFieldPill key={f} field={f} />
            ))}
          </div>
        </div>
        <button
          onClick={() => onRevalidate(item.profileId)}
          disabled={isPending}
          className="shrink-0 text-xs text-white bg-blue-600 hover:bg-blue-700 disabled:bg-blue-300 disabled:cursor-not-allowed px-3 py-1.5 rounded-md transition-colors"
        >
          {isPending ? 'Enqueuing…' : 'Revalidate now'}
        </button>
      </div>
    </div>
  );
}

// ─── Page ────────────────────────────────────────────────────────────────────

export function ValidationDashboardPage() {
  const [page, setPage] = useState(0);
  const [feedback, setFeedback] = useState<{ kind: 'ok' | 'err'; message: string } | null>(null);

  const { data: stats, isLoading: statsLoading } = useValidationStats();
  const { data: queue, isLoading, isError } = useValidationQueue({ page, pageSize: 20 });
  const revalidate = useRevalidateProfile();

  function handleRevalidate(profileId: string) {
    setFeedback(null);
    revalidate.mutate(profileId, {
      onSuccess: () =>
        setFeedback({ kind: 'ok', message: 'Profile queued for re-validation.' }),
      onError: (err: unknown) => {
        const message =
          err instanceof Error && err.message
            ? err.message
            : 'Failed to enqueue re-validation.';
        setFeedback({ kind: 'err', message });
      },
    });
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Validation Dashboard</h1>
        <p className="text-sm text-gray-500 mt-1">
          Re-validation engine metrics · queue updates live via SignalR
        </p>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        {statsLoading ? (
          <div className="col-span-4 h-16 bg-gray-100 rounded-xl animate-pulse" />
        ) : stats ? (
          <>
            <StatCard
              label="Pending re-validation"
              value={stats.pendingCount}
              color="text-blue-600"
            />
            <StatCard
              label="Processed today"
              value={stats.processedToday}
              color="text-green-600"
            />
            <StatCard
              label="Changes detected today"
              value={stats.changesDetectedToday}
              color="text-orange-600"
            />
            <StatCard
              label="Average data age"
              value={stats.averageDataAgeDays}
              suffix="days"
              color="text-gray-700"
            />
          </>
        ) : null}
      </div>

      {/* Feedback */}
      {feedback && (
        <div
          role="status"
          className={`rounded-lg px-4 py-2 text-sm ${
            feedback.kind === 'ok'
              ? 'bg-green-50 text-green-700 ring-1 ring-green-200'
              : 'bg-red-50 text-red-700 ring-1 ring-red-200'
          }`}
        >
          {feedback.message}
        </div>
      )}

      {/* Queue header */}
      <div>
        <h2 className="text-lg font-semibold text-gray-800">Queue</h2>
        <p className="text-xs text-gray-500 mt-1">
          Profiles with at least one expired field TTL. Counts are an upper bound —
          the scheduler picks items in priority order.
        </p>
      </div>

      {/* Queue list */}
      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-20 bg-gray-100 rounded-lg animate-pulse" />
          ))}
        </div>
      ) : isError ? (
        <div className="text-center py-12 text-red-500">Failed to load the queue.</div>
      ) : !queue || queue.items.length === 0 ? (
        <div className="text-center py-12 text-gray-400">
          No profiles are pending re-validation right now.
        </div>
      ) : (
        <>
          <div className="space-y-2">
            {queue.items.map((item) => (
              <QueueRow
                key={item.profileId}
                item={item}
                onRevalidate={handleRevalidate}
                isPending={revalidate.isPending && revalidate.variables === item.profileId}
              />
            ))}
          </div>
          <Pagination page={queue.page} totalPages={queue.totalPages} onPageChange={setPage} />
        </>
      )}
    </div>
  );
}
