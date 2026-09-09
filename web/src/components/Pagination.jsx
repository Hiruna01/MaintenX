import Button from './Button';

/** Previous / next paging over a PagedResult from the API. Pages are 1-based. */
export function Pagination({ page, totalPages, totalCount, onPageChange }) {
  const safeTotalPages = Math.max(totalPages, 1);

  return (
    <nav className="pagination" aria-label="Pagination">
      <Button variant="secondary" onClick={() => onPageChange(page - 1)} disabled={page <= 1}>
        Previous
      </Button>
      <span className="pagination__status">
        Page {page} of {safeTotalPages}
        {typeof totalCount === 'number' ? ` · ${totalCount} total` : ''}
      </span>
      <Button
        variant="secondary"
        onClick={() => onPageChange(page + 1)}
        disabled={page >= safeTotalPages}
      >
        Next
      </Button>
    </nav>
  );
}

export default Pagination;
