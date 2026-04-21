import { NavLink, Outlet } from 'react-router';
import { useSignalR } from '../hooks/useSignalR';
import { useChangeFeedLiveUpdates } from '../hooks/useChanges';
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
  const { connectionState, onChangeDetected, onValidationProgress } = useSignalR();

  // Invalidate change-feed and validation caches when SignalR pushes events.
  // Calling these hooks here (rather than in their pages) avoids creating
  // second useSignalR() consumers and the lifecycle-handler conflicts that
  // would cause — the singleton pattern requires a single owner.
  useChangeFeedLiveUpdates(onChangeDetected);
  useValidationLiveUpdates(onValidationProgress);

  return (
    <div className="flex h-screen bg-gray-50">
      {/* Sidebar */}
      <aside className="w-64 bg-gray-900 text-white flex flex-col">
        <div className="p-4 border-b border-gray-700">
          <h1 className="text-xl font-bold">Tracer</h1>
          <p className="text-xs text-gray-400 mt-1">Company Enrichment Engine</p>
        </div>
        <nav className="flex-1 p-2">
          {navItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === '/'}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2 rounded-lg mb-1 text-sm transition-colors ${
                  isActive
                    ? 'bg-blue-600 text-white'
                    : 'text-gray-300 hover:bg-gray-800 hover:text-white'
                }`
              }
            >
              <span>{item.icon}</span>
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
      <main className="flex-1 overflow-auto">
        <div className="p-6">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
