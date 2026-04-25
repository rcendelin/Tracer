import type { TraceStatus } from '../types';

const statusConfig: Record<TraceStatus, { label: string; className: string }> = {
  Pending: { label: 'Pending', className: 'bg-yellow-100 text-yellow-800' },
  InProgress: { label: 'In Progress', className: 'bg-blue-100 text-blue-800' },
  Completed: { label: 'Completed', className: 'bg-green-100 text-green-800' },
  PartiallyCompleted: { label: 'Partial', className: 'bg-orange-100 text-orange-800' },
  Failed: { label: 'Failed', className: 'bg-red-100 text-red-800' },
  Cancelled: { label: 'Cancelled', className: 'bg-gray-100 text-gray-800' },
  Queued: { label: 'Queued', className: 'bg-purple-100 text-purple-800' },
};

export function StatusBadge({ status }: { status: TraceStatus }) {
  const config = statusConfig[status] ?? { label: status, className: 'bg-gray-100 text-gray-800' };

  return (
    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${config.className}`}>
      {config.label}
    </span>
  );
}
