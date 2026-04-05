import type { SourceResult, SourceStatus } from '../types';

const statusIcon: Record<SourceStatus, string> = {
  Success: '✅',
  NotFound: '🔍',
  Error: '❌',
  Timeout: '⏱️',
  Skipped: '⏭️',
  Unknown: '❓',
};

const statusColor: Record<SourceStatus, string> = {
  Success: 'border-green-500',
  NotFound: 'border-gray-400',
  Error: 'border-red-500',
  Timeout: 'border-yellow-500',
  Skipped: 'border-gray-300',
  Unknown: 'border-gray-300',
};

export function SourceTimeline({ sources }: { sources?: SourceResult[] }) {
  if (!sources || sources.length === 0) {
    return <p className="text-gray-500 text-sm">No source results available.</p>;
  }

  return (
    <div className="space-y-3">
      {sources.map((source, index) => (
        <div key={index} className={`flex items-start gap-3 pl-4 border-l-4 ${statusColor[source.status]} py-2`}>
          <span className="text-lg">{statusIcon[source.status]}</span>
          <div className="flex-1">
            <div className="flex items-center justify-between">
              <span className="font-medium text-sm text-gray-900">{source.providerId}</span>
              <span className="text-xs text-gray-500">{source.durationMs}ms</span>
            </div>
            <div className="flex items-center gap-3 mt-1 text-xs text-gray-500">
              <span>{source.status}</span>
              {source.fieldsEnriched > 0 && (
                <span className="text-green-600">{source.fieldsEnriched} fields enriched</span>
              )}
              {source.errorMessage && (
                <span className="text-red-600">{source.errorMessage}</span>
              )}
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}
