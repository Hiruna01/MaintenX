import { SearchX, UserPlus, Users } from 'lucide-react';
import { useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import PageHeader from '../../../components/ui/PageHeader';
import Pager from '../../../components/ui/Pager';
import { Panel } from '../../../components/ui/Panel';
import Segmented from '../../../components/ui/Segmented';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import useDebounce from '../../../hooks/useDebounce';
import useAuth from '../../auth/hooks/useAuth';
import UserRows from '../components/UserRows';
import UserToolbar from '../components/UserToolbar';
import useUsers from '../hooks/useUsers';
import useUserStatusCounts from '../hooks/useUserStatusCounts';
import { DEFAULT_PAGE_SIZE, USER_STATUSES } from '../services/usersApi';
import styles from '../users.module.css';

const EMPTY_FILTERS = { search: '', role: '', status: USER_STATUSES.All };

const STATUS_TABS = [
  { value: USER_STATUSES.All, label: 'All' },
  { value: USER_STATUSES.Active, label: 'Active' },
  { value: USER_STATUSES.Deactivated, label: 'Deactivated' },
];

function RowsSkeleton() {
  return (
    <div className={styles.rowsSkeleton} role="status" aria-label="Loading users">
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className={styles.rowSkeleton}>
          <Skeleton width={32} height={12} />
          <div style={{ flex: 1, display: 'grid', gap: 6 }}>
            <Skeleton width="36%" height={14} />
            <Skeleton width="28%" height={11} />
          </div>
          <Skeleton width={96} height={24} radius={999} />
          <Skeleton width={84} height={24} radius={999} />
          <Skeleton width={80} height={12} />
        </div>
      ))}
    </div>
  );
}

/**
 * The fetched part of the page — the status tabs with their counts, the rows and the pager.
 * The page remounts it with a new `key` after any change in a panel, which is how the list
 * reloads (there is no refetch in useFetch), while the filters, held above it, survive.
 */
function UserResults({ filters, page, onPageChange, onFilterChange, onClear, toolbar, currentUserId }) {
  const { data, isLoading, error } = useUsers({ ...filters, page, pageSize: DEFAULT_PAGE_SIZE });
  const counts = useUserStatusCounts({ search: filters.search, role: filters.role });
  const hasFilters = Boolean(filters.search || filters.role || filters.status);

  return (
    <>
      <div className={styles.listControls}>
        <Segmented
          label="Filter by account status"
          value={filters.status}
          onChange={(value) => onFilterChange('status', value)}
          options={STATUS_TABS.map((tab) => ({ ...tab, count: counts[tab.value] }))}
        />
        {toolbar}
      </div>

      {isLoading ? <RowsSkeleton /> : null}

      {!isLoading && error ? (
        <Panel>
          <ErrorState title="Could not load users" message={error.message} />
        </Panel>
      ) : null}

      {!isLoading && !error && data ? (
        data.items.length === 0 ? (
          <Panel>
            <EmptyState
              icon={hasFilters ? SearchX : Users}
              title={hasFilters ? 'No accounts match these filters' : 'No accounts yet'}
              body={
                hasFilters
                  ? 'Try a different search, or clear the filters to see everyone.'
                  : 'Create the first staff account to get started.'
              }
              action={
                hasFilters ? (
                  <MxButton onClick={onClear}>Clear filters</MxButton>
                ) : (
                  <MxButton variant="primary" icon={UserPlus} to="/users/new">
                    Create account
                  </MxButton>
                )
              }
            />
          </Panel>
        ) : (
          <>
            <UserRows users={data.items} currentUserId={currentUserId} />
            <Pager
              page={data.page}
              pageSize={data.pageSize}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={onPageChange}
              noun={data.totalCount === 1 ? 'account' : 'accounts'}
            />
          </>
        )
      ) : null}
    </>
  );
}

/**
 * User management — Admin only, exactly the API's policy on every /api/users action but the
 * technician picker. Create, edit, deactivate, reactivate and reset a password; all of them
 * open as slide-overs on top of this list (/users/new, /users/:id), rendered into the Outlet.
 *
 * "Delete" is deactivate: the account stops signing in and its history stays. The client
 * decides none of it — which accounts exist, whether a change is allowed — it shows what the
 * API sent and the API's refusals as worded.
 */
export function UsersPage() {
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [page, setPage] = useState(1);
  const location = useLocation();
  const { user } = useAuth();

  // One request after typing stops, instead of one per keystroke.
  const debouncedSearch = useDebounce(filters.search, 400);
  const refresh = location.state?.refresh ?? 0;

  function handleFilterChange(name, value) {
    setFilters((current) => ({ ...current, [name]: value }));
    // A new filter means a new result set, so page 3 of the old one is meaningless.
    setPage(1);
  }

  function handleClear() {
    setFilters(EMPTY_FILTERS);
    setPage(1);
  }

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Estate' }, { label: 'Users' }]}
        title="Users"
        lead="Everyone who can sign in — their role, and whether their account is active. Deactivating keeps their history."
        actions={
          <MxButton variant="primary" icon={UserPlus} to="/users/new">
            Create account
          </MxButton>
        }
      />

      <UserResults
        key={refresh}
        filters={{ ...filters, search: debouncedSearch }}
        page={page}
        onPageChange={setPage}
        onFilterChange={handleFilterChange}
        onClear={handleClear}
        currentUserId={user?.id}
        toolbar={<UserToolbar values={filters} onChange={handleFilterChange} onClear={handleClear} />}
      />

      {/* /users/new and /users/:id render their slide-over here, over the list. */}
      <Outlet />
    </section>
  );
}

export default UsersPage;
