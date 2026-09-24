import { useState } from 'react';

import ErrorMessage from '../../../components/ErrorMessage';
import Pagination from '../../../components/Pagination';
import Spinner from '../../../components/Spinner';
import useDebounce from '../../../hooks/useDebounce';
import VerificationFilters from '../components/VerificationFilters';
import VerificationTable from '../components/VerificationTable';
import useVerifications from '../hooks/useVerifications';
import { DEFAULT_PAGE_SIZE, VERIFICATION_SORTS } from '../services/verificationApi';

const EMPTY_FILTERS = { search: '', status: '', dateFrom: '', dateTo: '' };

/**
 * Every verification check — did each repair actually hold? — latest due first by default.
 *
 * WHO SEES WHICH CHECKS is not decided here: the API gives a manager every check and a Reporter
 * the checks on faults they reported, from the token, whatever this page asks for.
 */
export function VerificationsPage() {
  // Local UI state stays in useState; only auth/session is app-wide Context.
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sort, setSort] = useState(VERIFICATION_SORTS.DueAt);
  const [page, setPage] = useState(1);

  // One request after typing stops, instead of one per keystroke.
  const debouncedSearch = useDebounce(filters.search, 400);

  const { data, isLoading, error } = useVerifications({
    ...filters,
    search: debouncedSearch,
    sort,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });

  // "YYYY-MM-DD" strings compare correctly as strings — no Date object needed, or wanted.
  const dateRangeError =
    filters.dateFrom && filters.dateTo && filters.dateFrom > filters.dateTo
      ? '"Due from" is after "Due to", so no check can match. Both ends are inclusive.'
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
          <p className="page-header__eyebrow">Verification</p>
          <h1>Did the repair hold?</h1>
          <p className="page__lead">
            A few days after every completed job, the reporter is asked whether the fault is
            really gone. Overdue checks are the ones still waiting past their deadline.
          </p>
        </div>
      </header>

      <VerificationFilters
        values={filters}
        onChange={handleFilterChange}
        onClear={handleClear}
        dateRangeError={dateRangeError}
      />

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading checks…" /> : null}

      {!isLoading && error ? (
        <ErrorMessage title="Could not load verification checks" message={error.message} />
      ) : null}

      {!isLoading && !error && data ? (
        <>
          {/* An empty list is not a failure and must not look like one. */}
          {data.items.length === 0 ? (
            <div className="empty-state">
              <p className="empty-state__title">
                {hasFilters ? 'No checks match these filters' : 'No verification checks yet'}
              </p>
              <p className="empty-state__body">
                {hasFilters
                  ? 'Try a different tag, status or date range, or clear the filters.'
                  : 'A check is raised every time a work order is completed, and falls due a few days later.'}
              </p>
            </div>
          ) : (
            <VerificationTable checks={data.items} sort={sort} onSortChange={handleSortChange} />
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

export default VerificationsPage;
