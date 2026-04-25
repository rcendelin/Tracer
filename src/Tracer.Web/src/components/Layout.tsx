import { useCallback, useState } from 'react';
import { NavLink, Outlet } from 'react-router';
import { useSignalR } from '../hooks/useSignalR';
import { useChangeFeedLiveUpdates } from '../hooks/useChanges';
import { useGlobalToasts } from '../hooks/useGlobalToasts';
import { useMediaQuery } from '../hooks/useMediaQuery';
import { useValidationLiveUpdates } from '../hooks/useValidation';
import { ConnectionStatusBadge } from './ConnectionStatusBadge';

const navItems = [
  { to: '/', label: 'Dashboard', icon: '📊' },
  { to: '/traces', label: 'Traces', icon: '🔍' },
  { to: '/trace/new', label: 'New Trace', icon: '➕' },
  { to: '/profiles', label: 'Profiles', icon: '🏢' },
  { to: '/changes', label: 'Change Feed', icon: '🔔' },
  { to: '/validation', label: 'Validation', icon: '✅' },
];

export function Layout() {
  // Initialise the shared SignalR connection at layout level so it stays
  // alive for the entire session and is available to all child pages.
  const { connectionState, onChangeDetected, onTraceCompleted, onValidationProgress } = useSignalR();

  // Invalidate change-feed and validation caches when SignalR pushes events.
  // Calling these hooks here (rather than in their pages) avoids creating
  // second useSignalR() consumers and the lifecycle-handler conflicts that
  // would cause — the singleton pattern requires a single owner.
  useChangeFeedLiveUpdates(onChangeDetected);
  useValidationLiveUpdates(onValidationProgress);

  // Global toasts for TraceCompleted and Critical/Major ChangeDetected.
  // Mounted here so the user sees a notification regardless of page.
  useGlobalToasts({ onTraceCompleted, onChangeDetected });

  const isDesktop = useMediaQuery('(min-width: 768px)');
  const [mobileSidebarOpen, setMobileSidebarOpen] = useState(false);

  // Derived display flag: the mobile sidebar is never "open" on desktop
  // layouts, and the overlay/transform classes consume this derived value
  // rather than the raw state. This avoids a useEffect to reset state on
  // viewport / route changes (which would trip react-hooks/set-state-in-effect).
  const sidebarOpen = !isDesktop && mobileSidebarOpen;

  const toggleSidebar = useCallback(() => setMobileSidebarOpen((v) => !v), []);
  const closeSidebar = useCallback(() => setMobileSidebarOpen(false), []);

  return (
    <div className="flex min-h-screen bg-gray-50">
      {/* Skip-link — visually hidden until focused. */}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-50 focus:px-3 focus:py-2 focus:bg-blue-600 focus:text-white focus:rounded focus:shadow-lg"
      >
        Skip to main content
      </a>

      {/* Mobile overlay — clicking it closes the sidebar. */}
      {!isDesktop && sidebarOpen && (
        <button
          type="button"
          aria-label="Close navigation menu"
          onClick={closeSidebar}
          className="fixed inset-0 z-30 bg-black/40 md:hidden"
        />
      )}

      {/* Sidebar — fixed overlay on mobile, static column from md up. */}
      <aside
        id="primary-navigation"
        aria-label="Primary navigation"
        // When the sidebar is off-screen on mobile it must not receive focus
        // or be announced by AT; `inert` hides it from the tab order and
        // accessibility tree in a single prop (React 19 / baseline browsers).
        inert={!isDesktop && !mobileSidebarOpen}
        className={`
          bg-gray-900 text-white flex flex-col
          fixed inset-y-0 left-0 z-40 w-64 transform transition-transform duration-200 ease-in-out
          ${sidebarOpen ? 'translate-x-0' : '-translate-x-full'}
          md:static md:translate-x-0 md:flex md:h-auto md:min-h-screen md:flex-shrink-0
        `}
      >
        <div className="p-4 border-b border-gray-700 flex items-center justify-between">
          <div>
            <h1 className="text-xl font-bold">Tracer</h1>
            <p className="text-xs text-gray-400 mt-1">Company Enrichment Engine</p>
          </div>
          {!isDesktop && (
            <button
              type="button"
              aria-label="Close navigation menu"
              onClick={closeSidebar}
              className="md:hidden text-gray-400 hover:text-white focus:outline-none focus:ring-2 focus:ring-blue-500 rounded p-1"
            >
              <span aria-hidden="true" className="text-lg">✕</span>
            </button>
          )}
        </div>
        <nav className="flex-1 p-2" aria-label="Main">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              onClick={closeSidebar}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2 rounded-lg mb-1 text-sm transition-colors focus:outline-none focus:ring-2 focus:ring-blue-500 ${
                  isActive
                    ? 'bg-blue-600 text-white'
                    : 'text-gray-300 hover:bg-gray-800 hover:text-white'
                }`
              }
            >
              <span aria-hidden="true">{item.icon}</span>
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>
        <div className="p-4 border-t border-gray-700 flex items-center justify-between">
          <span className="text-xs text-gray-500">Phase 2</span>
          <ConnectionStatusBadge state={connectionState} />
        </div>
      </aside>

      {/* Main content */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* Mobile top bar — only visible on small viewports. */}
        {!isDesktop && (
          <header className="md:hidden sticky top-0 z-20 bg-white border-b border-gray-200 flex items-center gap-3 px-4 py-3">
            <button
              type="button"
              aria-label="Open navigation menu"
              aria-expanded={sidebarOpen}
              aria-controls="primary-navigation"
              onClick={toggleSidebar}
              className="p-1 rounded focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <span aria-hidden="true" className="text-xl">☰</span>
            </button>
            <span className="font-semibold text-gray-900">Tracer</span>
            <span className="ml-auto">
              <ConnectionStatusBadge state={connectionState} />
            </span>
          </header>
        )}

        <main
          id="main-content"
          tabIndex={-1}
          className="flex-1 overflow-auto p-4 md:p-6 focus:outline-none"
        >
          <Outlet />
        </main>
      </div>
    </div>
  );
}
