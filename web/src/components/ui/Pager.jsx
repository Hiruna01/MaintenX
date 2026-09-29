import clsx from 'clsx';
import { ChevronLeft, ChevronRight } from 'lucide-react';

import styles from './ui.module.css';

/** Which page numbers to show: always the first and last, and a window around the current one. */
function pageWindow(page, totalPages) {
  const pages = new Set([1, totalPages, page - 1, page, page + 1]);
  const sorted = [...pages].filter((p) => p >= 1 && p <= totalPages).sort((a, b) => a - b);
  const out = [];
  sorted.forEach((p, i) => {
    if (i > 0 && p - sorted[i - 1] > 1) out.push(`gap-${p}`);
    out.push(p);
  });
  return out;
}

/** Paging over the API's PagedResult. Pages are 1-based. */
export function Pager({ page, pageSize, totalPages, totalCount, onPageChange, noun = 'items' }) {
  const safeTotal = Math.max(totalPages, 1);
  const first = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
  const last = Math.min(page * pageSize, totalCount);

  return (
    <nav className={styles.pager} aria-label="Pagination">
      <span className={styles.pagerRange}>
        {first}–{last} of {totalCount} {noun}
      </span>
      <div className={styles.pagerPages}>
        <button
          type="button"
          className={styles.pagerPage}
          onClick={() => onPageChange(page - 1)}
          disabled={page <= 1}
          aria-label="Previous page"
        >
          <ChevronLeft aria-hidden="true" />
        </button>
        {pageWindow(page, safeTotal).map((p) =>
          typeof p === 'string' ? (
            <span key={p} aria-hidden="true">
              …
            </span>
          ) : (
            <button
              key={p}
              type="button"
              className={clsx(styles.pagerPage, p === page && styles.pagerPageActive)}
              onClick={() => onPageChange(p)}
              aria-current={p === page ? 'page' : undefined}
              aria-label={`Page ${p}`}
            >
              {p}
            </button>
          ),
        )}
        <button
          type="button"
          className={styles.pagerPage}
          onClick={() => onPageChange(page + 1)}
          disabled={page >= safeTotal}
          aria-label="Next page"
        >
          <ChevronRight aria-hidden="true" />
        </button>
      </div>
    </nav>
  );
}

export default Pager;
