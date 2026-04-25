import { EmptyState } from '../components/EmptyState';

export function NotFoundPage() {
  return (
    <div className="py-16">
      <EmptyState
        icon="🧭"
        title="404 — Page not found"
        description="The link you followed doesn't match any route in Tracer. It may have been renamed or the URL is incorrect."
        action={{ label: 'Back to Dashboard', to: '/' }}
      />
    </div>
  );
}
