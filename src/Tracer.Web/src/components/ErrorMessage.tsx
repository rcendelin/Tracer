interface ErrorMessageProps {
  /** Short description shown to the user. Keep PII-free. */
  title?: string;
  /** Underlying error surfaced from `useQuery` or similar. Never rendered as HTML. */
  error: unknown;
  /** Optional retry handler — renders a Retry button when provided. */
  onRetry?: () => void;
  className?: string;
}

function messageOf(error: unknown): string {
  if (error instanceof Error) return error.message;
  if (typeof error === 'string') return error;
  return 'Unknown error';
}

/**
 * Standardised inline error block. Replaces the scattered
 * `bg-red-50 border-red-200 …` snippets in the page components so the
 * Retry affordance is uniform and ARIA-role correct.
 *
 * Uses `role="alert"` so assistive tech announces errors as soon as they
 * render.
 */
export function ErrorMessage({
  title = 'Something went wrong',
  error,
  onRetry,
  className = '',
}: ErrorMessageProps) {
  return (
    <div
      role="alert"
      className={`bg-red-50 border border-red-200 rounded-lg p-4 text-red-700 text-sm flex items-start gap-3 ${className}`}
    >
      <span className="text-xl leading-none" aria-hidden="true">⚠️</span>
      <div className="flex-1 min-w-0">
        <p className="font-medium text-red-800">{title}</p>
        <p className="mt-0.5 break-words">{messageOf(error)}</p>
      </div>
      {onRetry && (
        <button
          type="button"
          onClick={onRetry}
          className="shrink-0 px-3 py-1 rounded-md bg-white border border-red-300 text-red-700 text-xs font-medium hover:bg-red-50 focus:outline-none focus:ring-2 focus:ring-red-500"
        >
          Retry
        </button>
      )}
    </div>
  );
}
