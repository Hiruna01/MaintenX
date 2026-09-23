import { useState } from 'react';

import ErrorMessage from '../../../components/ErrorMessage';
import Pagination from '../../../components/Pagination';
import Spinner from '../../../components/Spinner';
import useDebounce from '../../../hooks/useDebounce';
import ReportFilters from '../components/ReportFilters';
import ReportTable from '../components/ReportTable';
import useReports from '../hooks/useReports';
import { DEFAULT_PAGE_SIZE, REPORT_SORTS } from '../services/reportsApi';

const EMPTY_FILTERS = { search: '', status: '', dateFrom: '', dateTo: '' };

/**
 * The intake queue: every fault reported on the estate, newest first by default.
 *
 * Routed for managers only, but WHO SEES WHICH REPORTS is not decided here — the API scopes
 * a Reporter to their own reports from the token, whatever this page asks for. A manager
 * sees the estate because the API says so, not because this page asked nicely.
 */
export function ReportsPage() {
  // Local UI state stays in useState; only auth/session is app-wide Context.
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sort, setSort] = useState(REPORT_SORTS.CreatedAt);
  const [page, setPage] = useState(1);

  // One request after typing stops, instead of one per keystroke. The search is a real
  // server-side parameter, matched across every page.
  const debouncedSearch = useDebounce(filters.search, 400);

  const { data, isLoading, error } = useReports({
    ...filters,
    search: debouncedSearch,
    sort,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });

  // "YYYY-MM-DD" strings compare correctly as strings, so no Date object is needed — and
  // none is wanted, since `new Date("2026-09-01")` is UTC midnight and can move a day.
  const dateRangeError =
    filters.dateFrom && filters.dateTo && filters.dateFrom > filters.dateTo
      ? '"From" is after "To", so no report can match. Both ends are inclusive.'
      : null;

  function handleFilterChange(name, value) {
    setFilters((current) => ({ ...current, [name]: value }));
    // A new filter means a new result set, so page 3 of the old one is meaningless.
    setPage(1);
  }

  function handleClear() {
    setFilters(EMPTY_FILTERS);
    setPage(1);
  }

  function handleSortChange(nextSort) {
    setSort(nextSort);
    setPage(1);
  }

  const hasFilters = Object.values(filters).some(Boolean);

  return (
    <section className="page">
      <header className="page-header">
        <div>
          <p className="page-header__eyebrow">Intake queue</p>
          <h1>Reports</h1>
          <p className="page__lead">
            Faults reported on the estate, and where each one has got to. Open a report to see
            what the agents did with it.
          </p>
        </div>
      </header>

      <ReportFilters
        values={filters}
        onChange={handleFilterChange}
        onClear={handleClear}
        dateRangeError={dateRangeError}
      />

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading reports…" /> : null}

      {!isLoading && error ? (
        <ErrorMessage title="Could not load reports" message={error.message} />
      ) : null}

      {!isLoading && !error && data ? (
        <>
          {/* An empty list is not a failure and must not look like one. */}
          {data.items.length === 0 ? (
            <div className="empty-state">
              <p className="empty-state__title">
                {hasFilters ? 'No reports match these filters' : 'No reports yet'}
              </p>
              <p className="empty-state__body">
                {hasFilters
                  ? 'Try a different search or date range, or clear the filters to see the whole queue.'
                  : 'Reports appear here as soon as someone files one from the mobile app.'}
              </p>
            </div>
          ) : (
            <ReportTable reports={data.items} sort={sort} onSortChange={handleSortChange} />
          )}

          {data.totalCount > 0 ? (
            <Pagination
              page={data.page}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={setPage}
            />
          ) : null}
        </>
      ) : null}
    </section>
  );
}

export default ReportsPage;
