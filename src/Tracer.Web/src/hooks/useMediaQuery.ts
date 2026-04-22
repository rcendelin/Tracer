import { useEffect, useState } from 'react';

/**
 * Subscribes to a CSS `matchMedia` query and returns whether it currently
 * matches. Used for responsive JS behaviour that mirrors Tailwind breakpoints
 * (e.g. closing a mobile sidebar overlay once the viewport grows ≥ `md`).
 *
 * SSR-safe: returns `false` when `window` is not available.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState<boolean>(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return false;
    }
    return window.matchMedia(query).matches;
  });

  useEffect(() => {
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') {
      return;
    }
    const mql = window.matchMedia(query);
    const handler = (event: MediaQueryListEvent) => setMatches(event.matches);
    // The useState initializer already captured the initial match value;
    // subscribe to subsequent changes only. (Re-syncing here would violate
    // react-hooks/set-state-in-effect for a negligible race-condition win.)
    mql.addEventListener('change', handler);
    return () => mql.removeEventListener('change', handler);
  }, [query]);

  return matches;
}
