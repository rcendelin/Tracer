import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Routes, Route } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Layout } from './components/Layout';
import { DashboardPage } from './pages/DashboardPage';
import { TracesPage } from './pages/TracesPage';
import { TraceDetailPage } from './pages/TraceDetailPage';
import { NewTracePage } from './pages/NewTracePage';
import { ProfilesPage } from './pages/ProfilesPage';
import { ProfileDetailPage } from './pages/ProfileDetailPage';
import { ChangeFeedPage } from './pages/ChangeFeedPage';
import { NotFoundPage } from './pages/NotFoundPage';
import './index.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route index element={<DashboardPage />} />
            <Route path="traces" element={<TracesPage />} />
            <Route path="traces/:traceId" element={<TraceDetailPage />} />
            <Route path="trace/new" element={<NewTracePage />} />
            <Route path="profiles" element={<ProfilesPage />} />
            <Route path="profiles/:profileId" element={<ProfileDetailPage />} />
            <Route path="changes" element={<ChangeFeedPage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
