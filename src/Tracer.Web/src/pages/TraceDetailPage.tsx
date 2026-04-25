import { useEffect, useState } from 'react';
import { useParams, Link } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import { useTraceDetail } from '../hooks/useTraceDetail';
import { useSignalR } from '../hooks/useSignalR';
import { StatusBadge } from '../components/StatusBadge';
import { ConfidenceBar } from '../components/ConfidenceBar';
import { FieldRow } from '../components/FieldRow';
import { SourceTimeline } from '../components/SourceTimeline';
import { SkeletonCard, SkeletonTable } from '../components/skeleton/Skeleton';
import { ErrorMessage } from '../components/ErrorMessage';
import { EmptyState } from '../components/EmptyState';
import type { Address, SourceResult, TraceStatus } from '../types';

function formatAddress(addr: Address): string {
  const parts = [addr.street, addr.city, addr.postalCode, addr.country].filter(Boolean);
  return addr.formattedAddress ?? parts.join(', ');
}

/** Live state overlaid on top of the REST-fetched trace while it is still running. */
interface LiveState {
  sources: SourceResult[];
  status: TraceStatus;
  overallConfidence?: number;
  durationMs?: number;
}

export function TraceDetailPage() {
  const { traceId } = useParams<{ traceId: string }>();
  const queryClient = useQueryClient();
  const { data: trace, isLoading, isError, error, refetch } = useTraceDetail(traceId);
  const { subscribeToTrace, onSourceCompleted, onTraceCompleted } = useSignalR();

  // Accumulate live source results pushed via SignalR while the trace is running.
  const [live, setLive] = useState<LiveState | null>(null);

  // Reset live overlay when navigating to a different trace — without this,
  // React Router reusing this component would show stale sources from the
  // previous traceId until a SignalR event arrives.
  useEffect(() => {
    setLive(null);
  }, [traceId]);

  // Join the per-trace SignalR group so the server sends SourceCompleted and
  // TraceCompleted only to clients watching this specific trace.
  useEffect(() => {
    if (!traceId) return;
    return subscribeToTrace(traceId);
  }, [traceId, subscribeToTrace]);

  // Initialise live state from the REST response as a baseline so sources that
  // arrived before this page mounted are visible immediately.
  useEffect(() => {
    if (trace && !live) {
      setLive({
        sources: trace.sources ?? [],
        status: trace.status,
        overallConfidence: trace.overallConfidence,
        durationMs: trace.durationMs,
      });
    }
  }, [trace, live]);

  // Subscribe to SourceCompleted — append the new source to the live timeline.
  useEffect(() => {
    return onSourceCompleted((event) => {
      if (event.traceId !== traceId) return;
      setLive((prev) => {
        if (!prev) return prev;
        const alreadyPresent = prev.sources.some(
          (s) => s.providerId === event.source.providerId,
        );
        if (alreadyPresent) return prev;
        return { ...prev, sources: [...prev.sources, event.source] };
      });
    });
  }, [traceId, onSourceCompleted]);

  // Subscribe to TraceCompleted — update status/confidence, then refetch the
  // full trace from the API to get the final enriched company data.
  useEffect(() => {
    return onTraceCompleted((event) => {
      if (event.traceId !== traceId) return;
      setLive((prev) =>
        prev
          ? {
              ...prev,
              status: event.status,
              overallConfidence: event.overallConfidence ?? prev.overallConfidence,
              durationMs: event.durationMs ?? prev.durationMs,
            }
          : prev,
      );
      // Invalidate the REST query so the full enriched profile loads.
      void queryClient.invalidateQueries({ queryKey: ['trace', traceId] });
    });
  }, [traceId, onTraceCompleted, queryClient]);

  if (isLoading) {
    return (
      <div className="space-y-6">
        <SkeletonCard />
        <SkeletonCard lines={3} />
        <SkeletonTable rows={6} columns={['w-1/6', 'w-2/6', 'w-1/6', 'w-1/6', 'w-1/6']} />
      </div>
    );
  }

  if (isError) {
    return (
      <ErrorMessage
        title="Could not load trace"
        error={error}
        onRetry={() => void refetch()}
      />
    );
  }

  if (!trace) {
    return (
      <EmptyState
        icon="🔍"
        title="Trace not found"
        description="It may have been archived, or the link you followed is incorrect."
        action={{ label: 'Back to Traces', to: '/traces' }}
      />
    );
  }

  const formatDate = (dateStr?: string) =>
    dateStr ? new Date(dateStr).toLocaleString('cs-CZ') : '-';

  // Merge REST data with live overlay — live takes precedence for in-flight fields.
  const displayStatus = live?.status ?? trace.status;
  const displayConfidence = live?.overallConfidence ?? trace.overallConfidence;
  const displayDuration = live?.durationMs ?? trace.durationMs;
  const displaySources = live?.sources ?? trace.sources;

  const isRunning = displayStatus === 'Pending' || displayStatus === 'InProgress';

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <Link to="/traces" className="text-sm text-blue-600 hover:text-blue-800 mb-2 inline-block">
            &larr; Back to Traces
          </Link>
          <h2 className="text-2xl font-bold text-gray-900">
            {trace.company?.legalName?.value ?? 'Enrichment Result'}
          </h2>
          <p className="text-sm text-gray-500 mt-1 font-mono">{trace.traceId}</p>
        </div>
        <div className="text-right space-y-2">
          <div className="flex items-center gap-2 justify-end">
            <StatusBadge status={displayStatus} />
            {isRunning && (
              <span className="inline-flex items-center gap-1 text-xs text-blue-600 font-medium">
                <span className="h-2 w-2 rounded-full bg-blue-500 animate-pulse" />
                Live
              </span>
            )}
          </div>
          <div className="mt-2">
            <ConfidenceBar value={displayConfidence} />
          </div>
        </div>
      </div>

      {/* Metadata */}
      <div className="bg-white rounded-lg shadow p-4">
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
          <div>
            <span className="text-gray-500">Created</span>
            <p className="font-medium">{formatDate(trace.createdAt)}</p>
          </div>
          <div>
            <span className="text-gray-500">Completed</span>
            <p className="font-medium">{formatDate(trace.completedAt)}</p>
          </div>
          <div>
            <span className="text-gray-500">Duration</span>
            <p className="font-medium">{displayDuration ? `${displayDuration}ms` : '-'}</p>
          </div>
          <div>
            <span className="text-gray-500">Status</span>
            <p className="font-medium">{displayStatus}</p>
          </div>
        </div>
        {trace.failureReason && (
          <div className="mt-3 p-3 bg-red-50 rounded text-sm text-red-700">
            <strong>Failure:</strong> {trace.failureReason}
          </div>
        )}
      </div>

      {/* Enriched Fields */}
      {trace.company && (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <div className="px-4 py-3 bg-gray-50 border-b">
            <h3 className="text-lg font-semibold text-gray-900">Enriched Fields</h3>
          </div>
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Field</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Value</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Confidence</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Source</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Enriched</th>
              </tr>
            </thead>
            <tbody>
              <FieldRow label="Legal Name" field={trace.company.legalName} />
              <FieldRow label="Trade Name" field={trace.company.tradeName} />
              <FieldRow label="Tax ID" field={trace.company.taxId} />
              <FieldRow label="Legal Form" field={trace.company.legalForm} />
              <FieldRow label="Entity Status" field={trace.company.entityStatus} />
              <FieldRow label="Industry" field={trace.company.industry} />
              <FieldRow label="Phone" field={trace.company.phone} />
              <FieldRow label="Email" field={trace.company.email} />
              <FieldRow label="Website" field={trace.company.website} />
              <FieldRow label="Parent Company" field={trace.company.parentCompany} />
              <FieldRow label="Employee Range" field={trace.company.employeeRange} />
              <FieldRow
                label="Registered Address"
                field={trace.company.registeredAddress}
                renderValue={(v) => formatAddress(v as Address)}
              />
              <FieldRow
                label="Operating Address"
                field={trace.company.operatingAddress}
                renderValue={(v) => formatAddress(v as Address)}
              />
              <FieldRow
                label="Location"
                field={trace.company.location}
                renderValue={(v) => {
                  const loc = v as { latitude: number; longitude: number };
                  return `${loc.latitude.toFixed(4)}, ${loc.longitude.toFixed(4)}`;
                }}
              />
            </tbody>
          </table>
        </div>
      )}

      {/* Source Timeline — shows live updates as providers complete */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b flex items-center justify-between">
          <h3 className="text-lg font-semibold text-gray-900">Source Results</h3>
          {isRunning && (
            <span className="text-xs text-blue-600 flex items-center gap-1.5">
              <span className="h-2 w-2 rounded-full bg-blue-500 animate-pulse" />
              Receiving live updates
            </span>
          )}
        </div>
        <div className="p-4">
          <SourceTimeline sources={displaySources} />
        </div>
      </div>
    </div>
  );
}
