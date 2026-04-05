import { useParams } from 'react-router';

export function TraceDetailPage() {
  const { traceId } = useParams<{ traceId: string }>();

  return (
    <div>
      <h2 className="text-2xl font-bold text-gray-900 mb-4">Trace Detail</h2>
      <p className="text-gray-500">Trace ID: {traceId}</p>
      <p className="text-gray-500">Detail view will be implemented in B-26.</p>
    </div>
  );
}
