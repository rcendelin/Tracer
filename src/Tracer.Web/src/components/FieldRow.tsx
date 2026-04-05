import type { TracedField } from '../types';
import { ConfidenceBar } from './ConfidenceBar';

interface FieldRowProps {
  label: string;
  field?: TracedField<unknown> | null;
  renderValue?: (value: unknown) => string;
}

export function FieldRow({ label, field, renderValue }: FieldRowProps) {
  if (!field) return null;

  const displayValue = renderValue
    ? renderValue(field.value)
    : typeof field.value === 'object'
      ? JSON.stringify(field.value)
      : String(field.value);

  const formatDate = (dateStr: string) => {
    return new Date(dateStr).toLocaleString('cs-CZ', { dateStyle: 'short', timeStyle: 'short' });
  };

  return (
    <tr className="border-b last:border-b-0">
      <td className="px-4 py-3 text-sm font-medium text-gray-500 w-40">{label}</td>
      <td className="px-4 py-3 text-sm text-gray-900">{displayValue}</td>
      <td className="px-4 py-3"><ConfidenceBar value={field.confidence} /></td>
      <td className="px-4 py-3">
        <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-indigo-100 text-indigo-800">
          {field.source}
        </span>
      </td>
      <td className="px-4 py-3 text-xs text-gray-500">{formatDate(field.enrichedAt)}</td>
    </tr>
  );
}
