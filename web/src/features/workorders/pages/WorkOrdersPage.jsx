import { useState } from 'react';
import { Link } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Pagination from '../../../components/Pagination';
import Spinner from '../../../components/Spinner';
import useDebounce from '../../../hooks/useDebounce';
import useAuth from '../../auth/hooks/useAuth';
import { DISPATCH_ROLES, hasRole } from '../../auth/services/roles';
import WorkOrderFilters from '../components/WorkOrderFilters';
import WorkOrderTable from '../components/WorkOrderTable';
import useWorkOrders from '../hooks/useWorkOrders';
import { DEFAULT_PAGE_SIZE, WORK_ORDER_SORTS } from '../services/workOrdersApi';

const EMPTY_FILTERS = { search: '', status: '', technicianId: '' };

/**
 * The dispatch board: live and finished work, newest first by default, or by the largest
 * estimate. Filter by status and — for a manager — by technician; search by asset tag or
 * fault, server-side and debounced.
 *
 * WHO SEES WHICH ORDERS is not decided here. The API gives a Technician their own queue and a
 * manager the estate, from the token, whatever this page asks for.
 */
export function WorkOrdersPage() {
  const { role } = useAuth();
  const isDispatcher = hasRole(role, DISPATCH_ROLES);

  // Local UI state stays in useState; only auth/session is app-wide Context.
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sort, setSort] = useState(WORK_ORDER_SORTS.CreatedAt);
  const [page, setPage] = useState(1);

  // One request after typing stops, instead of one per keystroke.
  const debouncedSearch = useDebounce(filters.search, 400);

  const { data, isLoading, error } = useWorkOrders({
    ...filters,
    search: debouncedSearch,
    sort,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });

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
          <p className="page-header__eyebrow">{isDispatcher ? 'Dispatch board' : 'Your queue'}</p>
          <h1>Work orders</h1>
          <p className="page__lead">
            {isDispatcher
              ? 'Every order on the estate — who is going, when, and what it costs. Open one to assign it, book a visit or see how it ended.'
              : 'The work assigned to you. Open an order to see when it is booked and to complete it.'}
          </p>
        </div>
        {isDispatcher ? (
          <Link to="/approvals" className="button button--secondary page-header__action">
            Approval queue →
          </Link>
        ) : null}
      </header>

      <WorkOrderFilters
        values={filters}
        onChange={handleFilterChange}
        onClear={handleClear}
        showTechnicianFilter={isDispatcher}
      />

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading work orders…" /> : null}

      {!isLoading && error ? (
        <ErrorMessage title="Could not load work orders" message={error.message} />
      ) : null}

      {!isLoading && !error && data ? (
        <>
          {/* An empty list is not a failure and must not look like one. */}
          {data.items.length === 0 ? (
            <div className="empty-state">
              <p className="empty-state__title">
                {hasFilters ? 'No work orders match these filters' : 'No work orders yet'}
              </p>
              <p className="empty-state__body">
                {hasFilters
                  ? 'Try a different search or status, or clear the filters to see the whole board.'
                  : isDispatcher
                    ? 'Orders appear here once they are raised from a report.'
                    : 'Nothing is assigned to you right now.'}
              </p>
            </div>
          ) : (
            <WorkOrderTable workOrders={data.items} sort={sort} onSortChange={handleSortChange} />
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

export default WorkOrdersPage;
