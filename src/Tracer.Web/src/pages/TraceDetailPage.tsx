import { useParams, Link } from 'react-router';
import { useTraceDetail } from '../hooks/useTraceDetail';
import { StatusBadge } from '../components/StatusBadge';
import { ConfidenceBar } from '../components/ConfidenceBar';
import { FieldRow } from '../components/FieldRow';
import { SourceTimeline } from '../components/SourceTimeline';
import type { Address } from '../types';

function formatAddress(addr: Address): string {
  const parts = [addr.street, addr.city, addr.postalCode, addr.country].filter(Boolean);
  return addr.formattedAddress ?? parts.join(', ');
}

export function TraceDetailPage() {
  const { traceId } = useParams<{ traceId: string }>();
  const { data: trace, isLoading, isError, error } = useTraceDetail(traceId);

  if (isLoading) return <div className="text-center py-10 text-gray-500">Loading...</div>;

  if (isError) {
    return (
      <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700 text-sm">
        {error instanceof Error ? error.message : 'Failed to load trace'}
      </div>
    );
  }

  if (!trace) return <div className="text-center py-10 text-gray-500">Trace not found</div>;

  const formatDate = (dateStr?: string) =>
    dateStr ? new Date(dateStr).toLocaleString('cs-CZ') : '-';

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
          <StatusBadge status={trace.status} />
          <div className="mt-2">
            <ConfidenceBar value={trace.overallConfidence} />
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
            <p className="font-medium">{trace.durationMs ? `${trace.durationMs}ms` : '-'}</p>
          </div>
          <div>
            <span className="text-gray-500">Status</span>
            <p className="font-medium">{trace.status}</p>
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

      {/* Source Timeline */}
      <div className="bg-white rounded-lg shadow overflow-hidden">
        <div className="px-4 py-3 bg-gray-50 border-b">
          <h3 className="text-lg font-semibold text-gray-900">Source Results</h3>
        </div>
        <div className="p-4">
          <SourceTimeline sources={trace.sources} />
        </div>
      </div>
    </div>
  );
}
