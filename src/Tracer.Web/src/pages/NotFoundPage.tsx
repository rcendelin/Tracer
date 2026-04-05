import { Link } from 'react-router';

export function NotFoundPage() {
  return (
    <div className="text-center py-20">
      <h2 className="text-4xl font-bold text-gray-900 mb-4">404</h2>
      <p className="text-gray-500 mb-6">Page not found</p>
      <Link to="/" className="text-blue-600 hover:text-blue-800 underline">
        Back to Dashboard
      </Link>
    </div>
  );
}
