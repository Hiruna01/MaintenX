import { Search, SearchX, ShieldCheck, X } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import Notice from '../../../components/ui/Notice';
import DateRange from '../../../components/ui/DateRange';
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
import RunSweepButton from '../components/RunSweepButton';
import VerificationRows from '../components/VerificationRows';
import VerificationSummary from '../components/VerificationSummary';
import useVerifications from '../hooks/useVerifications';
import useVerificationStatusCounts from '../hooks/useVerificationStatusCounts';
import {
  DEFAULT_PAGE_SIZE,
  VERIFICATION_SORTS,
  VERIFICATION_STATUSES,
  describeSweepResult,
  enumLabel,
} from '../services/verificationApi';
import styles from '../verification.module.css';

const EMPTY_FILTERS = { search: '', status: '', dateFrom: '', dateTo: '' };

// The two members of VerificationSort, no direction: latest due first, or grouped by the
// stored status name alphabetically.
const SORT_OPTIONS = [
  { value: VERIFICATION_SORTS.DueAt, label: 'Latest due' },
  { value: VERIFICATION_SORTS.Status, label: 'Status (A–Z)' },
];

const TAB_LABELS = { AwaitingReporterResponse: 'Awaiting reporter' };

function RowsSkeleton() {
  return (
    <div className={styles.skeleton} role="status" aria-label="Loading checks">
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className={styles.skeletonRow}>
          <Skeleton width={28} height={12} />
          <div style={{ flex: 1, display: 'grid', gap: 8 }}>
            <Skeleton width="60%" height={14} />
            <Skeleton width={150} height={22} radius={8} />
          </div>
          <Skeleton width={110} height={24} radius={999} />
          <Skeleton width={100} height={24} radius={999} />
          <Skeleton width={90} height={12} />
        </div>
      ))}
    </div>
  );
}

/**
 * Every verification check — did each repair actually hold? — latest due first by default.
 *
 * WHO SEES WHICH CHECKS is not decided here: the API gives a manager every check and a Reporter
 * the checks on faults they reported, from the token, whatever this page asks for.
 */
export function VerificationsPage() {
  const { role } = useAuth();
  const isDispatcher = hasRole(role, DISPATCH_ROLES);

  // Filters live here, above the keyed body, so a refresh after a sweep keeps them.
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sort, setSort] = useState(VERIFICATION_SORTS.DueAt);
  const [page, setPage] = useState(1);

  // A sweep changes statuses under the list, the tab counts and the summary. Bumping this
  // remounts all three — a fresh mount is a fresh useFetch, which is the whole of the refetch.
  const [version, setVersion] = useState(0);
  const [sweepMessage, setSweepMessage] = useState(null);
  const [sweepError, setSweepError] = useState(null);

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

  function handleSwept(result) {
    setSweepError(null);
    setSweepMessage(describeSweepResult(result));
    setVersion((current) => current + 1);
  }

  function handleSweepError(message) {
    setSweepMessage(null);
    setSweepError(message);
  }

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Operations' }, { label: 'Verification' }]}
        title="Did the repair hold?"
        lead="A few days after every completed job, the reporter is asked whether the fault is really gone. Overdue checks are the ones still waiting past their deadline."
        actions={isDispatcher ? <RunSweepButton onSwept={handleSwept} onError={handleSweepError} /> : null}
      />

      {/* Keyed by the run, so a notice dismissed after one sweep shows again after the next. */}
      {sweepMessage ? <Notice key={version}>{sweepMessage}</Notice> : null}
      {sweepError ? (
        <p className={styles.hintError} role="alert">
          The sweep could not run: {sweepError}
        </p>
      ) : null}

      <VerificationsBody
        key={version}
        isDispatcher={isDispatcher}
        filters={filters}
        sort={sort}
        page={page}
        onFilterChange={handleFilterChange}
        onClear={handleClear}
        onSortChange={handleSortChange}
        onPageChange={setPage}
      />
    </section>
  );
}

/** Everything the API is read for: the summary, the tab counts and the list. */
function VerificationsBody({ isDispatcher, filters, sort, page, onFilterChange, onClear, onSortChange, onPageChange }) {
  // One request after typing stops, instead of one per keystroke.
  const debouncedSearch = useDebounce(filters.search, 400);

  const { data, isLoading, error } = useVerifications({ ...filters, search: debouncedSearch, sort, page, pageSize: DEFAULT_PAGE_SIZE });
  const counts = useVerificationStatusCounts({ ...filters, search: debouncedSearch });

  // "YYYY-MM-DD" strings compare correctly as strings — no Date object needed, or wanted.
  const dateRangeError =
    filters.dateFrom && filters.dateTo && filters.dateFrom > filters.dateTo
      ? '“From” is after “To”, so no check can match. Both ends are inclusive.'
      : null;

  const hasFilters = Object.values(filters).some(Boolean);

  return (
    <>
      {/* The loop's own numbers are a facilities manager's read, like the endpoint behind them. */}
      {isDispatcher ? <VerificationSummary /> : null}

      <div className={styles.controls}>
        <Segmented
          label="Filter by status"
          value={filters.status}
          onChange={(value) => onFilterChange('status', value)}
          options={[
            { value: '', label: 'All', count: counts[''] },
            ...VERIFICATION_STATUSES.map((status) => ({
              value: status,
              label: TAB_LABELS[status] ?? enumLabel(status),
              count: counts[status],
            })),
          ]}
        />

        <div className={styles.toolbar}>
          <label className={styles.search}>
            <span className="mx-visually-hidden">Search by asset tag</span>
            <Search aria-hidden="true" />
            <input
              type="search"
              placeholder="Search by asset tag"
              value={filters.search}
              onChange={(event) => onFilterChange('search', event.target.value)}
              autoComplete="off"
            />
          </label>
          <DateRange
            label="Due"
            from={filters.dateFrom}
            to={filters.dateTo}
            onFromChange={(value) => onFilterChange('dateFrom', value)}
            onToChange={(value) => onFilterChange('dateTo', value)}
            invalid={Boolean(dateRangeError)}
          />
          <SelectMenu ariaLabel="Sort checks" inlineLabel="Sort" value={sort} onChange={onSortChange} options={SORT_OPTIONS} />
          {hasFilters ? (
            <MxButton variant="ghost" icon={X} onClick={onClear}>
              Clear
            </MxButton>
          ) : null}
        </div>

        <p className={dateRangeError ? styles.hintError : styles.hint} role={dateRangeError ? 'alert' : undefined}>
          {dateRangeError ?? 'Due dates are UTC days, and both ends are inclusive.'}
        </p>
      </div>

      {isLoading ? <RowsSkeleton /> : null}

      {!isLoading && error ? (
        <Panel>
          <ErrorState title="Could not load verification checks" message={error.message} />
        </Panel>
      ) : null}

      {!isLoading && !error && data ? (
        data.items.length === 0 ? (
          <Panel>
            <EmptyState
              icon={hasFilters ? SearchX : ShieldCheck}
              title={hasFilters ? 'No checks match these filters' : 'No verification checks yet'}
              body={
                hasFilters
                  ? 'Try a different tag, status or date range, or clear the filters.'
                  : 'A check is raised every time a work order is completed, and falls due a few days later.'
              }
              action={hasFilters ? <MxButton onClick={onClear}>Clear filters</MxButton> : null}
            />
          </Panel>
        ) : (
          <>
            <VerificationRows checks={data.items} />
            <Pager
              page={data.page}
              pageSize={data.pageSize}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={onPageChange}
              noun={data.totalCount === 1 ? 'check' : 'checks'}
            />
          </>
        )
      ) : null}
    </>
  );
}

export default VerificationsPage;
