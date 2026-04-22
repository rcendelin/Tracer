import type { ReactNode } from 'react';
import { Link } from 'react-router';

interface EmptyStateProps {
  /** Large visual cue, typically an emoji or short SVG. */
  icon?: ReactNode;
  /** Heading describing why the area is empty. */
  title: string;
  /** Supporting copy, plain text or JSX. */
  description?: ReactNode;
  /**
   * Primary action. Either a `to` prop (renders `<Link>`) or an `onClick`
   * (renders `<button>`). If both are omitted, no action is rendered.
   */
  action?:
    | { label: string; to: string }
    | { label: string; onClick: () => void };
  className?: string;
}

/**
 * Reusable empty-state block. Standardises the "no data yet" copy and CTA
 * placement across list pages. Rendered as `role="status"` so AT consumers
 * hear the message when it appears after a data fetch.
 */
export function EmptyState({
  icon = '📭',
  title,
  description,
  action,
  className = '',
}: EmptyStateProps) {
  return (
    <div
      role="status"
      className={`text-center py-12 px-4 ${className}`}
    >
      <div className="text-5xl mb-3" aria-hidden="true">
        {icon}
      </div>
      <h3 className="text-base font-semibold text-gray-900">{title}</h3>
      {description && (
        <p className="mt-1 text-sm text-gray-500 max-w-md mx-auto">
          {description}
        </p>
      )}
      {action && (
        <div className="mt-4">
          {'to' in action ? (
            <Link
              to={action.to}
              className="inline-block px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors"
            >
              {action.label}
            </Link>
          ) : (
            <button
              type="button"
              onClick={action.onClick}
              className="inline-block px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors"
            >
              {action.label}
            </button>
          )}
        </div>
      )}
    </div>
  );
}
