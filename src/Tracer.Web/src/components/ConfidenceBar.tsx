export function ConfidenceBar({ value }: { value?: number }) {
  if (value === undefined || value === null) {
    return <span className="text-gray-400 text-sm">-</span>;
  }

  const percent = Math.round(value * 100);
  const color =
    percent >= 80 ? 'bg-green-500' :
    percent >= 60 ? 'bg-yellow-500' :
    percent >= 40 ? 'bg-orange-500' :
    'bg-red-500';

  return (
    <div className="flex items-center gap-2">
      <div className="w-20 h-2 bg-gray-200 rounded-full overflow-hidden">
        <div className={`h-full rounded-full ${color}`} style={{ width: `${percent}%` }} />
      </div>
      <span className="text-xs text-gray-600 w-8">{percent}%</span>
    </div>
  );
}
