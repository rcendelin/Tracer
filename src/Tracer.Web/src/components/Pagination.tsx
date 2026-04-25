interface PaginationProps {
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

export function Pagination({ page, totalPages, onPageChange }: PaginationProps) {
  if (totalPages <= 1) return null;

  const atFirst = page === 0;
  const atLast = page >= totalPages - 1;

  return (
    <nav
      aria-label="Pagination"
      className="flex items-center justify-between mt-4"
    >
      <p className="text-sm text-gray-500" aria-live="polite">
        Page {page + 1} of {totalPages}
      </p>
      <div className="flex gap-2">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={atFirst}
          aria-label="Previous page"
          className="px-3 py-1 text-sm border rounded-md disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          Previous
        </button>
        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={atLast}
          aria-label="Next page"
          className="px-3 py-1 text-sm border rounded-md disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-blue-500"
        >
          Next
        </button>
      </div>
    </nav>
  );
}
