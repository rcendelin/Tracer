interface PaginationProps {
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

export function Pagination({ page, totalPages, onPageChange }: PaginationProps) {
  if (totalPages <= 1) return null;

  return (
    <div className="flex items-center justify-between mt-4">
      <p className="text-sm text-gray-500">
        Page {page + 1} of {totalPages}
      </p>
      <div className="flex gap-2">
        <button
          onClick={() => onPageChange(page - 1)}
          disabled={page === 0}
          className="px-3 py-1 text-sm border rounded-md disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50"
        >
          Previous
        </button>
        <button
          onClick={() => onPageChange(page + 1)}
          disabled={page >= totalPages - 1}
          className="px-3 py-1 text-sm border rounded-md disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50"
        >
          Next
        </button>
      </div>
    </div>
  );
}
