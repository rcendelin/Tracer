import type { ReactNode } from 'react';

/**
 * Base skeleton block — a pulsing grey rectangle sized by Tailwind classes.
 * Use the semantic wrappers below rather than this directly when possible.
 */
export function Skeleton({ className = '' }: { className?: string }) {
  return (
    <div
      className={`animate-pulse rounded bg-gray-200 ${className}`}
      aria-hidden="true"
    />
  );
}

/**
 * Single placeholder line. Mimics a line of text. Default width full, height 3.
 */
export function SkeletonLine({
  width = 'w-full',
  height = 'h-3',
  className = '',
}: {
  width?: string;
  height?: string;
  className?: string;
}) {
  return <Skeleton className={`${height} ${width} ${className}`} />;
}

/**
 * Placeholder card matching `.bg-white.rounded-lg.shadow.p-6`.
 * Use for metric/stat cards and small panels.
 */
export function SkeletonCard({
  lines = 2,
  className = '',
}: {
  lines?: number;
  className?: string;
}) {
  return (
    <div
      className={`bg-white rounded-lg shadow p-6 space-y-3 ${className}`}
      role="status"
      aria-label="Loading"
    >
      <SkeletonLine width="w-1/3" height="h-3" />
      {Array.from({ length: lines }).map((_, i) => (
        <SkeletonLine
          key={i}
          width={i === 0 ? 'w-2/3' : 'w-5/6'}
          height="h-5"
        />
      ))}
    </div>
  );
}

/**
 * Placeholder table matching the 4-column list layout used by Traces/Profiles.
 * `rows` controls the number of placeholder rows (default 5). `columns`
 * accepts Tailwind width classes to approximate real column widths.
 */
export function SkeletonTable({
  rows = 5,
  columns = ['w-1/6', 'w-1/3', 'w-1/6', 'w-1/4'],
  header = true,
  className = '',
}: {
  rows?: number;
  columns?: string[];
  header?: boolean;
  className?: string;
}) {
  return (
    <div
      className={`bg-white rounded-lg shadow overflow-hidden ${className}`}
      role="status"
      aria-label="Loading table"
    >
      {header && (
        <div className="bg-gray-50 border-b px-4 py-3 flex gap-4">
          {columns.map((w, i) => (
            <SkeletonLine key={i} width={w} height="h-3" />
          ))}
        </div>
      )}
      <div className="divide-y">
        {Array.from({ length: rows }).map((_, r) => (
          <div key={r} className="px-4 py-3 flex gap-4 items-center">
            {columns.map((w, c) => (
              <SkeletonLine key={c} width={w} height="h-4" />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

/**
 * Invisible SR-only label attached to a skeleton group so AT users are
 * told that content is loading rather than silently seeing nothing.
 */
export function SkeletonAnnouncement({ children = 'Loading…' }: { children?: ReactNode }) {
  return <span className="sr-only">{children}</span>;
}
