import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

interface State {
  error: Error | null;
}

/**
 * Top-level React error boundary. Catches uncaught render exceptions from
 * the whole route tree and shows a neutral fallback so users don't see a
 * blank page. Technical detail (stack) is exposed only in development.
 *
 * Registered in `main.tsx` around `<BrowserRouter>` so it also catches
 * faults in `Layout` / routing itself.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    // In dev we want the full detail in the console; in prod this is a
    // signal for future telemetry (e.g. App Insights browser SDK — TODO).
    // eslint-disable-next-line no-console
    console.error('[ErrorBoundary]', error, info);
  }

  handleReload = (): void => {
    // Full reload is the most reliable way to recover from an unexpected
    // render error — React 19 does not offer a supported API to re-mount
    // a subtree after a caught error.
    window.location.reload();
  };

  render(): ReactNode {
    const { error } = this.state;

    if (!error) {
      return this.props.children;
    }

    const isDev = import.meta.env.DEV;

    return (
      <div className="min-h-screen bg-gray-50 flex items-center justify-center p-6">
        <div
          role="alert"
          className="max-w-lg w-full bg-white rounded-lg shadow border border-gray-200 p-6 space-y-4"
        >
          <div className="flex items-center gap-3">
            <span className="text-3xl" aria-hidden="true">💥</span>
            <h1 className="text-xl font-bold text-gray-900">
              Something went wrong
            </h1>
          </div>
          <p className="text-sm text-gray-600">
            The Tracer UI hit an unexpected error. Reloading the page usually
            fixes it. If it keeps happening, let the team know.
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={this.handleReload}
              className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 transition-colors"
            >
              Reload page
            </button>
            <a
              href="/"
              className="px-4 py-2 bg-gray-100 text-gray-700 rounded-lg text-sm font-medium hover:bg-gray-200 transition-colors"
            >
              Back to Dashboard
            </a>
          </div>
          {isDev && (
            <details className="text-xs text-gray-500 mt-3">
              <summary className="cursor-pointer select-none">
                Technical detail (dev only)
              </summary>
              <pre className="mt-2 p-2 bg-gray-50 rounded overflow-x-auto max-h-64">
                {error.stack ?? error.message}
              </pre>
            </details>
          )}
        </div>
      </div>
    );
  }
}
