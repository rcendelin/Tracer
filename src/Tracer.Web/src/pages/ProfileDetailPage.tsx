import { useState } from 'react';
import { useParams, Link } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useProfileDetail } from '../hooks/useProfileDetail';
import { useProfileHistory } from '../hooks/useProfileHistory';
import { ConfidenceBar } from '../components/ConfidenceBar';
import { Pagination } from '../components/Pagination';
import { profileApi } from '../api/client';
import type { TracedField, Address, GeoCoordinate, ChangeSeverity, ChangeType, FieldName } from '../types';

// ── Helpers ─────────────────────────────────────────────────────────────────

function formatDate(dateStr?: string) {
  if (!dateStr) return '-';
  return new Date(dateStr).toLocaleString('cs-CZ', { dateStyle: 'short', timeStyle: 'short' });
}

function daysAgo(dateStr?: string): number | null {
  if (!dateStr) return null;
  return Math.floor((Date.now() - new Date(dateStr).getTime()) / (1000 * 60 * 60 * 24));
}

function AgeIndicator({ enrichedAt }: { enrichedAt?: string }) {
  const days = daysAgo(enrichedAt);
  if (days === null) return <span className="text-gray-400 text-xs">-</span>;
  const cls = days < 30 ? 'text-green-600' : days < 90 ? 'text-yellow-600' : 'text-red-600';
  return <span className={`text-xs ${cls}`}>{days}d ago</span>;
}

function SeverityBadge({ severity }: { severity: ChangeSeverity }) {
  const styles: Record<ChangeSeverity, string> = {
    Critical: 'bg-red-100 text-red-800',
    Major: 'bg-orange-100 text-orange-800',
    Minor: 'bg-yellow-100 text-yellow-700',
    Cosmetic: 'bg-gray-100 text-gray-600',
  };
  return (
    <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${styles[severity]}`}>
      {severity}
    </span>
  );
}

function ChangeTypeBadge({ changeType }: { changeType: ChangeType }) {
  const styles: Record<ChangeType, string> = {
    Created: 'bg-green-100 text-green-700',
    Updated: 'bg-blue-100 text-blue-700',
    Deleted: 'bg-red-100 text-red-700',
  };
  return (
    <span className={`inline-block px-1.5 py-0.5 rounded text-xs ${styles[changeType]}`}>
      {changeType}
    </span>
  );
}

function JsonDiff({ label, json }: { label: string; json?: string }) {
  if (!json) return null;
  let parsed: unknown;
  try { parsed = JSON.parse(json); } catch { parsed = json; }
  return (
    <div className="mt-1">
      <span className="text-xs text-gray-500">{label}:</span>
      <pre className="text-xs bg-gray-50 rounded p-1 mt-0.5 overflow-x-auto max-w-sm">
        {JSON.stringify(parsed, null, 2)}
      </pre>
    </div>
  );
}

function fieldLabel(field: FieldName): string {
  const labels: Partial<Record<FieldName, string>> = {
    LegalName: 'Legal Name', TradeName: 'Trade Name', RegistrationId: 'Reg. ID',
    TaxId: 'Tax ID', LegalForm: 'Legal Form', RegisteredAddress: 'Reg. Address',
    OperatingAddress: 'Op. Address', Phone: 'Phone', Email: 'Email', Website: 'Website',
    Industry: 'Industry', EmployeeRange: 'Employees', EntityStatus: 'Status',
    ParentCompany: 'Parent Co.', Location: 'Location', Officers: 'Officers',
  };
  return labels[field] ?? field;
}

function formatFieldValue(field: TracedField<unknown>): string {
  const v = field.value;
  if (typeof v === 'string') return v;
  if (v && typeof v === 'object') {
    // Address
    if ('street' in v) {
      const addr = v as Address;
      return [addr.street, addr.city, addr.postalCode, addr.country].filter(Boolean).join(', ');
    }
    // GeoCoordinate
    if ('latitude' in v) {
      const geo = v as GeoCoordinate;
      return `${geo.latitude.toFixed(4)}, ${geo.longitude.toFixed(4)}`;
    }
    return JSON.stringify(v);
  }
  return String(v);
}

// ── Main component ────────────────────────────────────────────────────────────

export function ProfileDetailPage() {
  const { profileId } = useParams<{ profileId: string }>();
  const queryClient = useQueryClient();
  const [historyPage, setHistoryPage] = useState(0);
  const [expandedChange, setExpandedChange] = useState<string | null>(null);

  const { data: detail, isLoading, isError, error } = useProfileDetail(profileId);
  const { data: history } = useProfileHistory(profileId, historyPage);

  const revalidateMutation = useMutation({
    mutationFn: () => profileApi.revalidate(profileId!),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['profile', profileId] });
    },
  });

  if (isLoading) return <div className="text-center py-10 text-gray-500">Loading...</div>;

  if (isError) {
    return (
      <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700 text-sm">
        {error instanceof Error ? error.message : 'Failed to load profile'}
      </div>
    );
  }

  if (!detail) return <div className="text-center py-10 text-gray-500">Profile not found</div>;

  const { profile } = detail;
  const enriched = profile.enriched;

  // Build field rows from enriched data
  type FieldEntry = { label: string; field: TracedField<unknown> };
  const fieldEntries: FieldEntry[] = [
    enriched?.legalName != null && { label: 'Legal Name', field: enriched.legalName as TracedField<unknown> },
    enriched?.tradeName != null && { label: 'Trade Name', field: enriched.tradeName as TracedField<unknown> },
    enriched?.taxId != null && { label: 'Tax ID', field: enriched.taxId as TracedField<unknown> },
    enriched?.legalForm != null && { label: 'Legal Form', field: enriched.legalForm as TracedField<unknown> },
    enriched?.entityStatus != null && { label: 'Entity Status', field: enriched.entityStatus as TracedField<unknown> },
    enriched?.industry != null && { label: 'Industry', field: enriched.industry as TracedField<unknown> },
    enriched?.employeeRange != null && { label: 'Employees', field: enriched.employeeRange as TracedField<unknown> },
    enriched?.phone != null && { label: 'Phone', field: enriched.phone as TracedField<unknown> },
    enriched?.email != null && { label: 'Email', field: enriched.email as TracedField<unknown> },
    enriched?.website != null && { label: 'Website', field: enriched.website as TracedField<unknown> },
    enriched?.parentCompany != null && { label: 'Parent Company', field: enriched.parentCompany as TracedField<unknown> },
    enriched?.registeredAddress != null && { label: 'Reg. Address', field: enriched.registeredAddress as TracedField<unknown> },
    enriched?.operatingAddress != null && { label: 'Op. Address', field: enriched.operatingAddress as TracedField<unknown> },
    enriched?.location != null && { label: 'Location', field: enriched.location as TracedField<unknown> },
  ].filter((e): e is FieldEntry => !!e);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <Link to="/profiles" className="text-sm text-blue-600 hover:text-blue-800 mb-2 inline-block">
            &larr; Back to Profiles
          </Link>
          <h2 className="text-2xl font-bold text-gray-900">
            {enriched?.legalName?.value ?? 'Unknown Company'}
          </h2>
          <p className="text-sm text-gray-500 mt-1 font-mono">{profile.normalizedKey}</p>
        </div>
        <div className="text-right space-y-2">
          <ConfidenceBar value={profile.overallConfidence} />
          <button
            onClick={() => revalidateMutation.mutate()}
            disabled={revalidateMutation.isPending}
            className="block ml-auto px-3 py-1.5 text-sm bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 transition-colors"
          >
            {revalidateMutation.isPending ? 'Queuing...' : 'Revalidate Now'}
          </button>
          {revalidateMutation.isSuccess && (
            <p className="text-xs text-green-600">Queued (Phase 3)</p>
          )}
          {revalidateMutation.isError && (
            <p className="text-xs text-red-600">Revalidation request failed</p>
          )}
        </div>
      </div>

      {/* Metadata */}
      <div className="bg-white rounded-lg shadow p-4">
        <div className="grid grid-cols-2 md:grid-cols-5 gap-4 text-sm">
          <div><span className="text-gray-500">Country</span><p className="font-medium font-mono">{profile.country}</p></div>
          <div><span className="text-gray-500">Reg. ID</span><p className="font-medium font-mono">{profile.registrationId ?? '-'}</p></div>
          <div><span className="text-gray-500">Trace Count</span><p className="font-medium">{profile.traceCount}</p></div>
          <div><span className="text-gray-500">Last Enriched</span><p className="font-medium">{formatDate(profile.lastEnrichedAt)}</p></div>
          <div><span className="text-gray-500">Last Validated</span><p className="font-medium">{formatDate(profile.lastValidatedAt)}</p></div>
        </div>
      </div>

      {/* Enriched Fields */}
      {fieldEntries.length > 0 && (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <div className="px-4 py-3 bg-gray-50 border-b">
            <h3 className="text-lg font-semibold text-gray-900">Enriched Fields</h3>
          </div>
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500 w-32">Field</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Value</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500 w-28">Confidence</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500 w-24">Source</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500 w-20">Age</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {fieldEntries.map(({ label, field }) => (
                <tr key={label}>
                  <td className="px-4 py-2 text-gray-500 font-medium">{label}</td>
                  <td className="px-4 py-2 text-gray-900 break-all">{formatFieldValue(field)}</td>
                  <td className="px-4 py-2"><ConfidenceBar value={field.confidence} /></td>
                  <td className="px-4 py-2">
                    <span className="inline-block px-2 py-0.5 bg-blue-50 text-blue-700 rounded text-xs font-mono">
                      {field.source}
                    </span>
                  </td>
                  <td className="px-4 py-2"><AgeIndicator enrichedAt={field.enrichedAt} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Recent Changes (from GetProfile — last 10) */}
      {detail.recentChanges.length > 0 && (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <div className="px-4 py-3 bg-gray-50 border-b flex items-center justify-between">
            <h3 className="text-lg font-semibold text-gray-900">Recent Changes</h3>
            <span className="text-xs text-gray-500">Last 10</span>
          </div>
          <div className="divide-y">
            {detail.recentChanges.map((change) => (
              <div key={change.id} className="px-4 py-3">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2 flex-wrap">
                    <SeverityBadge severity={change.severity} />
                    <ChangeTypeBadge changeType={change.changeType} />
                    <span className="font-medium text-sm text-gray-800">{fieldLabel(change.field)}</span>
                    <span className="text-xs text-gray-400">via {change.detectedBy}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-gray-400">{formatDate(change.detectedAt)}</span>
                    {(change.previousValueJson || change.newValueJson) && (
                      <button
                        onClick={() => setExpandedChange(expandedChange === change.id ? null : change.id)}
                        className="text-xs text-blue-600 hover:text-blue-800"
                      >
                        {expandedChange === change.id ? 'Hide diff' : 'Show diff'}
                      </button>
                    )}
                  </div>
                </div>
                {expandedChange === change.id && (
                  <div className="mt-2 grid grid-cols-2 gap-2">
                    <JsonDiff label="Before" json={change.previousValueJson} />
                    <JsonDiff label="After" json={change.newValueJson} />
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Full Change History (paged) */}
      {history && (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <div className="px-4 py-3 bg-gray-50 border-b flex items-center justify-between">
            <h3 className="text-lg font-semibold text-gray-900">Full Change History</h3>
            <span className="text-xs text-gray-500">{history.changes.totalCount} total</span>
          </div>
          <div className="divide-y">
            {history.changes.items.length === 0 && (
              <div className="text-center py-6 text-gray-500 text-sm">No change history</div>
            )}
            {history.changes.items.map((change) => (
              <div key={change.id} className="px-4 py-3">
                <div className="flex items-center gap-2 flex-wrap">
                  <SeverityBadge severity={change.severity} />
                  <ChangeTypeBadge changeType={change.changeType} />
                  <span className="font-medium text-sm text-gray-800">{fieldLabel(change.field)}</span>
                  <span className="text-xs text-gray-400">via {change.detectedBy}</span>
                  <span className="text-xs text-gray-400 ml-auto">{formatDate(change.detectedAt)}</span>
                </div>
              </div>
            ))}
          </div>
          <div className="px-4 pb-3">
            <Pagination page={historyPage} totalPages={history.changes.totalPages} onPageChange={setHistoryPage} />
          </div>
        </div>
      )}

      {/* Validation History */}
      {history && history.validations.length > 0 && (
        <div className="bg-white rounded-lg shadow overflow-hidden">
          <div className="px-4 py-3 bg-gray-50 border-b">
            <h3 className="text-lg font-semibold text-gray-900">Validation History</h3>
          </div>
          <table className="w-full text-sm">
            <thead className="bg-gray-50 border-b">
              <tr>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Date</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Type</th>
                <th className="text-left px-4 py-2 text-xs font-medium text-gray-500">Provider</th>
                <th className="text-right px-4 py-2 text-xs font-medium text-gray-500">Fields Checked</th>
                <th className="text-right px-4 py-2 text-xs font-medium text-gray-500">Changed</th>
                <th className="text-right px-4 py-2 text-xs font-medium text-gray-500">Duration</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {history.validations.map((v) => (
                <tr key={v.id}>
                  <td className="px-4 py-2 text-gray-600">{formatDate(v.validatedAt)}</td>
                  <td className="px-4 py-2">
                    <span className={`inline-block px-2 py-0.5 rounded text-xs ${
                      v.validationType === 'Deep' ? 'bg-purple-100 text-purple-700' : 'bg-gray-100 text-gray-600'
                    }`}>
                      {v.validationType}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-gray-600 font-mono text-xs">{v.providerId}</td>
                  <td className="px-4 py-2 text-right text-gray-600">{v.fieldsChecked}</td>
                  <td className="px-4 py-2 text-right">
                    <span className={v.fieldsChanged > 0 ? 'text-orange-600 font-medium' : 'text-gray-400'}>
                      {v.fieldsChanged}
                    </span>
                  </td>
                  <td className="px-4 py-2 text-right text-gray-600">{v.durationMs}ms</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
