import { useState } from 'react';
import { Link } from 'react-router';
import { Pagination } from '../components/Pagination';
import { useChanges, useChangeStats } from '../hooks/useChanges';
import { changesApi, type ExportFormat } from '../api/client';
import type { ChangeSeverity, ChangeEvent } from '../types';

// ─── Helpers ────────────────────────────────────────────────────────────────

function SeverityBadge({ severity }: { severity: ChangeSeverity }) {
  const styles: Record<ChangeSeverity, string> = {
    Critical: 'bg-red-100 text-red-700 ring-1 ring-red-300',
    Major:    'bg-orange-100 text-orange-700 ring-1 ring-orange-300',
    Minor:    'bg-yellow-100 text-yellow-700 ring-1 ring-yellow-300',
    Cosmetic: 'bg-gray-100 text-gray-600 ring-1 ring-gray-200',
  };
  return (
    <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${styles[severity]}`}>
      {severity}
    </span>
  );
}

function ChangeTypeBadge({ changeType }: { changeType: string }) {
  const styles: Record<string, string> = {
    Created: 'bg-green-100 text-green-700',
    Updated: 'bg-blue-100 text-blue-700',
    Deleted: 'bg-red-100 text-red-700',
  };
  return (
    <span className={`inline-block px-2 py-0.5 rounded-full text-xs ${styles[changeType] ?? 'bg-gray-100 text-gray-600'}`}>
      {changeType}
    </span>
  );
}

function relativeTime(isoString: string): string {
  const diffMs = Date.now() - new Date(isoString).getTime();
  const diffMin = Math.floor(diffMs / 60_000);
  if (diffMin < 1) return 'just now';
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffH = Math.floor(diffMin / 60);
  if (diffH < 24) return `${diffH}h ago`;
  return `${Math.floor(diffH / 24)}d ago`;
}

function JsonDiffRow({ label, json }: { label: string; json?: string }) {
  if (!json) return null;
  let parsed: unknown;
  try { parsed = JSON.parse(json); } catch { parsed = json; }
  return (
    <div className="mt-1">
      <span className="text-xs text-gray-400">{label}: </span>
      <code className="text-xs text-gray-600 bg-gray-50 rounded px-1 py-0.5 break-all">
        {typeof parsed === 'object' ? JSON.stringify(parsed) : String(parsed)}
      </code>
    </div>
  );
}

// ─── Stat card ───────────────────────────────────────────────────────────────

function StatCard({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div className="bg-white rounded-xl border border-gray-200 p-4 flex flex-col gap-1">
      <span className="text-xs text-gray-500">{label}</span>
      <span className={`text-2xl font-bold ${color}`}>{value.toLocaleString()}</span>
    </div>
  );
}

// ─── Severity filter tabs ────────────────────────────────────────────────────

const SEVERITY_TABS: { label: string; value: ChangeSeverity | undefined }[] = [
  { label: 'All', value: undefined },
  { label: 'Critical', value: 'Critical' },
  { label: 'Major', value: 'Major' },
  { label: 'Minor', value: 'Minor' },
  { label: 'Cosmetic', value: 'Cosmetic' },
];

// ─── Change event row ────────────────────────────────────────────────────────

function ChangeRow({ event, expanded, onToggle }: {
  event: ChangeEvent;
  expanded: boolean;
  onToggle: () => void;
}) {
  const hasDiff = event.previousValueJson != null || event.newValueJson != null;

  return (
    <div className="border border-gray-200 rounded-lg p-4 hover:border-gray-300 transition-colors">
      <div className="flex items-start gap-3">
        {/* Severity + field */}
        <div className="flex flex-col gap-1 min-w-0 flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <SeverityBadge severity={event.severity} />
            <ChangeTypeBadge changeType={event.changeType} />
            <span className="text-sm font-medium text-gray-800">{event.field}</span>
          </div>
          <div className="flex items-center gap-2 text-xs text-gray-500 flex-wrap">
            <Link
              to={`/profiles/${event.companyProfileId}`}
              className="text-blue-600 hover:text-blue-800 font-mono truncate max-w-[160px]"
              title={event.companyProfileId}
            >
              {event.companyProfileId.slice(0, 8)}…
            </Link>
            <span>·</span>
            <span>by <span className="font-medium text-gray-700">{event.detectedBy}</span></span>
            <span>·</span>
            <span title={new Date(event.detectedAt).toLocaleString()}>
              {relativeTime(event.detectedAt)}
            </span>
            {event.isNotified && (
              <>
                <span>·</span>
                <span className="text-green-600">notified</span>
              </>
            )}
          </div>
        </div>

        {/* Expand toggle */}
        {hasDiff && (
          <button
            onClick={onToggle}
            className="shrink-0 text-xs text-blue-600 hover:text-blue-800 px-2 py-1 rounded border border-blue-200 hover:bg-blue-50 transition-colors"
          >
            {expanded ? 'Hide diff' : 'Show diff'}
          </button>
        )}
      </div>

      {/* Expandable diff */}
      {expanded && hasDiff && (
        <div className="mt-3 pl-3 border-l-2 border-gray-200">
          <JsonDiffRow label="Before" json={event.previousValueJson} />
          <JsonDiffRow label="After" json={event.newValueJson} />
        </div>
      )}
    </div>
  );
}

// ─── Page ────────────────────────────────────────────────────────────────────

export function ChangeFeedPage() {
  const [page, setPage] = useState(0);
  const [severityFilter, setSeverityFilter] = useState<ChangeSeverity | undefined>(undefined);
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set());
  const [exportingFormat, setExportingFormat] = useState<ExportFormat | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);

  const { data: stats, isLoading: statsLoading } = useChangeStats();
  const { data: changes, isLoading, isError } = useChanges({
    page,
    pageSize: 20,
    severity: severityFilter,
  });

  async function handleExport(format: ExportFormat) {
    setExportingFormat(format);
    setExportError(null);
    try {
      await changesApi.export(format, { severity: severityFilter });
    } catch (err) {
      setExportError(err instanceof Error ? err.message : 'Export failed');
    } finally {
      setExportingFormat(null);
    }
  }

  function toggleExpand(id: string) {
    setExpandedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  }

  function handleSeverityChange(value: ChangeSeverity | undefined) {
    setSeverityFilter(value);
    setPage(0);
    setExpandedIds(new Set());
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Change Feed</h1>
          <p className="text-sm text-gray-500 mt-1">
            Field-level changes detected across all company profiles · updates live via SignalR
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <button
            type="button"
            disabled={exportingFormat !== null}
            onClick={() => handleExport('csv')}
            className="px-3 py-1.5 text-sm rounded-md border border-gray-300 bg-white text-gray-700 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-wait"
            title="Export current severity filter as CSV (up to 10 000 rows)"
          >
            {exportingFormat === 'csv' ? 'Exporting…' : 'Export CSV'}
          </button>
          <button
            type="button"
            disabled={exportingFormat !== null}
            onClick={() => handleExport('xlsx')}
            className="px-3 py-1.5 text-sm rounded-md border border-gray-300 bg-white text-gray-700 hover:bg-gray-50 disabled:opacity-50 disabled:cursor-wait"
            title="Export current severity filter as XLSX (up to 10 000 rows)"
          >
            {exportingFormat === 'xlsx' ? 'Exporting…' : 'Export XLSX'}
          </button>
        </div>
      </div>
      {exportError && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-red-700 text-sm">
          Export failed: {exportError}
        </div>
      )}

      {/* Stats */}
      <div className="grid grid-cols-2 sm:grid-cols-5 gap-3">
        {statsLoading ? (
          <div className="col-span-5 h-16 bg-gray-100 rounded-xl animate-pulse" />
        ) : stats ? (
          <>
            <StatCard label="Total"    value={stats.totalCount}    color="text-gray-800" />
            <StatCard label="Critical" value={stats.criticalCount} color="text-red-600" />
            <StatCard label="Major"    value={stats.majorCount}    color="text-orange-600" />
            <StatCard label="Minor"    value={stats.minorCount}    color="text-yellow-600" />
            <StatCard label="Cosmetic" value={stats.cosmeticCount} color="text-gray-500" />
          </>
        ) : null}
      </div>

      {/* Severity filter tabs */}
      <div className="flex gap-1 flex-wrap">
        {SEVERITY_TABS.map(tab => (
          <button
            key={tab.label}
            onClick={() => handleSeverityChange(tab.value)}
            className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-colors ${
              severityFilter === tab.value
                ? 'bg-blue-600 text-white'
                : 'bg-white text-gray-600 border border-gray-200 hover:bg-gray-50'
            }`}
          >
            {tab.label}
            {tab.value === 'Critical' && stats != null && stats.criticalCount > 0 && (
              <span className="ml-1.5 bg-red-500 text-white text-xs rounded-full px-1.5 py-0.5">
                {stats.criticalCount}
              </span>
            )}
          </button>
        ))}
      </div>

      {/* Change list */}
      {isLoading ? (
        <div className="space-y-3">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-16 bg-gray-100 rounded-lg animate-pulse" />
          ))}
        </div>
      ) : isError ? (
        <div className="text-center py-12 text-red-500">Failed to load changes.</div>
      ) : !changes || changes.items.length === 0 ? (
        <div className="text-center py-12 text-gray-400">
          {severityFilter
            ? `No ${severityFilter.toLowerCase()} changes found.`
            : 'No changes recorded yet.'}
        </div>
      ) : (
        <>
          <div className="space-y-2">
            {changes.items.map(event => (
              <ChangeRow
                key={event.id}
                event={event}
                expanded={expandedIds.has(event.id)}
                onToggle={() => toggleExpand(event.id)}
              />
            ))}
          </div>

          <Pagination
            page={changes.page}
            totalPages={changes.totalPages}
            onPageChange={setPage}
          />
        </>
      )}
    </div>
  );
}
