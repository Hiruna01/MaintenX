import { Search, SearchX, Stamp, Wrench, X } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import PageHeader from '../../../components/ui/PageHeader';
import Pager from '../../../components/ui/Pager';
import { Panel } from '../../../components/ui/Panel';
import Segmented from '../../../components/ui/Segmented';
import SelectMenu from '../../../components/ui/SelectMenu';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useDebounce from '../../../hooks/useDebounce';
import useAuth from '../../auth/hooks/useAuth';
import { DISPATCH_ROLES, hasRole } from '../../auth/services/roles';
import TechnicianSelect from '../components/TechnicianSelect';
import WorkOrderRows from '../components/WorkOrderRows';
import useWorkOrders from '../hooks/useWorkOrders';
import useWorkOrderStatusCounts from '../hooks/useWorkOrderStatusCounts';
import { DEFAULT_PAGE_SIZE, WORK_ORDER_SORTS, WORK_ORDER_STATUSES, enumLabel } from '../services/workOrdersApi';
import styles from '../workorders.module.css';

const EMPTY_FILTERS = { search: '', status: '', technicianId: '' };

// The two members of WorkOrderSort, no direction: newest first, or highest estimate first
// (sorted in C# as decimal).
const SORT_OPTIONS = [
  { value: WORK_ORDER_SORTS.CreatedAt, label: 'Newest first' },
  { value: WORK_ORDER_SORTS.Cost, label: 'Highest estimate' },
];

function RowsSkeleton() {
  return (
    <div className={styles.skeleton} role="status" aria-label="Loading work orders">
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className={styles.skeletonRow}>
          <Skeleton width={28} height={12} />
          <div style={{ flex: 1, display: 'grid', gap: 8 }}>
            <Skeleton width={140} height={24} radius={8} />
            <Skeleton width="30%" height={11} />
          </div>
          <Skeleton width={96} height={24} radius={999} />
          <Skeleton width={120} height={14} />
          <Skeleton width={80} height={14} />
        </div>
      ))}
    </div>
  );
}

/**
 * The dispatch board: live and finished work, newest first by default, or by the largest
 * estimate. Filter by status and — for a manager — by technician; search by asset tag or
 * fault, server-side and debounced.
 *
 * WHO SEES WHICH ORDERS is not decided here. The API gives a Technician their own queue and
 * a manager the estate, from the token, whatever this page asks for.
 */
export function WorkOrdersPage() {
  const { role } = useAuth();
  const isDispatcher = hasRole(role, DISPATCH_ROLES);

  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sort, setSort] = useState(WORK_ORDER_SORTS.CreatedAt);
  const [page, setPage] = useState(1);

  // One request after typing stops, instead of one per keystroke.
  const debouncedSearch = useDebounce(filters.search, 400);

  const { data, isLoading, error } = useWorkOrders({ ...filters, search: debouncedSearch, sort, page, pageSize: DEFAULT_PAGE_SIZE });
  const counts = useWorkOrderStatusCounts({ search: debouncedSearch, technicianId: filters.technicianId });

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
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Operations' }, { label: 'Work orders' }]}
        title={isDispatcher ? 'Dispatch board' : 'Your jobs'}
        lead={
          isDispatcher
            ? 'Every order on the estate — who is going, when, and what it costs. Open one to assign it, book a visit or see how it ended.'
            : 'The work assigned to you. Open an order to see when it is booked and to complete it.'
        }
        actions={
          isDispatcher ? (
            <MxButton icon={Stamp} to="/approvals">
              Approval queue
              {counts.AwaitingApproval ? <span className={styles.buttonCount}>{counts.AwaitingApproval}</span> : null}
            </MxButton>
          ) : null
        }
      />

      <div className={styles.controls}>
        <Segmented
          label="Filter by status"
          value={filters.status}
          onChange={(value) => handleFilterChange('status', value)}
          options={[
            { value: '', label: 'All', count: counts[''] },
            ...WORK_ORDER_STATUSES.map((status) => ({ value: status, label: enumLabel(status), count: counts[status] })),
          ]}
        />

        <div className={styles.toolbar}>
          <label className={styles.search}>
            <span className="mx-visually-hidden">Search work orders</span>
            <Search aria-hidden="true" />
            <input
              type="search"
              placeholder="Search by asset tag or fault"
              value={filters.search}
              onChange={(event) => handleFilterChange('search', event.target.value)}
              autoComplete="off"
            />
          </label>
          {/* Only a manager sees more than one technician's orders, so only a manager filters by one. */}
          {isDispatcher ? (
            <TechnicianSelect
              inlineLabel="Technician"
              emptyLabel="Anyone"
              value={filters.technicianId}
              onChange={(value) => handleFilterChange('technicianId', value)}
            />
          ) : null}
          <SelectMenu ariaLabel="Sort work orders" inlineLabel="Sort" value={sort} onChange={handleSortChange} options={SORT_OPTIONS} />
          {hasFilters ? (
            <MxButton variant="ghost" icon={X} onClick={handleClear}>
              Clear
            </MxButton>
          ) : null}
        </div>
      </div>

      {isLoading ? <RowsSkeleton /> : null}

      {!isLoading && error ? (
        <Panel>
          <ErrorState title="Could not load work orders" message={error.message} />
        </Panel>
      ) : null}

      {!isLoading && !error && data ? (
        data.items.length === 0 ? (
          <Panel>
            <EmptyState
              icon={hasFilters ? SearchX : Wrench}
              title={hasFilters ? 'No work orders match these filters' : isDispatcher ? 'No work orders yet' : 'Nothing assigned to you'}
              body={
                hasFilters
                  ? 'Try a different search or status, or clear the filters to see the whole board.'
                  : isDispatcher
                    ? 'Orders appear here once they are raised from a report.'
                    : 'Jobs appear here once a manager assigns them to you.'
              }
              action={hasFilters ? <MxButton onClick={handleClear}>Clear filters</MxButton> : null}
            />
          </Panel>
        ) : (
          <>
            <WorkOrderRows workOrders={data.items} />
            <Pager
              page={data.page}
              pageSize={data.pageSize}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={setPage}
              noun={data.totalCount === 1 ? 'order' : 'orders'}
            />
          </>
        )
      ) : null}
    </section>
  );
}

export default WorkOrdersPage;
