import { MessageSquareWarning, Search, SearchX, X } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import DateRange from '../../../components/ui/DateRange';
import PageHeader from '../../../components/ui/PageHeader';
import Pager from '../../../components/ui/Pager';
import { Panel } from '../../../components/ui/Panel';
import Segmented from '../../../components/ui/Segmented';
import SelectMenu from '../../../components/ui/SelectMenu';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import { humanize } from '../../../components/ui/tones';
import useDebounce from '../../../hooks/useDebounce';
import ReportRows from '../components/ReportRows';
import useReports from '../hooks/useReports';
import useReportStatusCounts from '../hooks/useReportStatusCounts';
import { DEFAULT_PAGE_SIZE, REPORT_SORTS, REPORT_STATUSES } from '../services/reportsApi';
import styles from '../reports.module.css';

const EMPTY_FILTERS = { search: '', status: '', dateFrom: '', dateTo: '' };

const STATUS_TABS = [
  { value: '', label: 'All' },
  ...REPORT_STATUSES.map((status) => ({
    value: status,
    label: status === 'WorkOrderRaised' ? 'Order raised' : humanize(status),
  })),
];

// The two members of ReportSort. The API takes no direction: newest first, or grouped by the
// stored status name alphabetically — not by lifecycle position.
const SORT_OPTIONS = [
  { value: REPORT_SORTS.CreatedAt, label: 'Newest first' },
  { value: REPORT_SORTS.Status, label: 'Status (A–Z)' },
];

function RowsSkeleton() {
  return (
    <div className={styles.skeleton} role="status" aria-label="Loading reports">
      {Array.from({ length: 6 }, (_, index) => (
        <div key={index} className={styles.skeletonRow}>
          <Skeleton width={28} height={12} />
          <div style={{ flex: 1, display: 'grid', gap: 7 }}>
            <Skeleton width="62%" height={14} />
            <Skeleton width="26%" height={11} />
          </div>
          <Skeleton width={120} height={24} radius={999} />
          <Skeleton width={92} height={12} />
        </div>
      ))}
    </div>
  );
}

/**
 * The intake queue: every fault reported on the estate, newest first by default.
 *
 * Routed for managers only, but WHO SEES WHICH REPORTS is not decided here — the API scopes
 * a Reporter to their own reports from the token, whatever this page asks for.
 */
export function ReportsPage() {
  const [filters, setFilters] = useState(EMPTY_FILTERS);
  const [sort, setSort] = useState(REPORT_SORTS.CreatedAt);
  const [page, setPage] = useState(1);

  const debouncedSearch = useDebounce(filters.search, 400);

  const { data, isLoading, error } = useReports({
    ...filters,
    search: debouncedSearch,
    sort,
    page,
    pageSize: DEFAULT_PAGE_SIZE,
  });
  const counts = useReportStatusCounts({ ...filters, search: debouncedSearch });

  // Compared as strings: both are "YYYY-MM-DD", so string order is date order.
  const dateRangeError =
    filters.dateFrom && filters.dateTo && filters.dateFrom > filters.dateTo
      ? '“From” is after “To”, so no report can match. Both ends are inclusive.'
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
  const hasNarrowing = Boolean(filters.search || filters.dateFrom || filters.dateTo);

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Estate' }, { label: 'Reports' }]}
        title="Reports"
        lead="Faults reported from the phone, and where each one has got to. Open one to see what the agents did with it."
        actions={
          counts[''] !== null ? (
            <span className={styles.headerCount}>
              <strong>{counts['']}</strong> {counts[''] === 1 ? 'report' : 'reports'}
              {hasNarrowing ? ' match' : ' on the estate'}
            </span>
          ) : null
        }
      />

      <div className={styles.controls}>
        <Segmented
          label="Filter by status"
          value={filters.status}
          onChange={(value) => handleFilterChange('status', value)}
          options={STATUS_TABS.map((tab) => ({ ...tab, count: counts[tab.value] }))}
        />

        <div className={styles.toolbar}>
          <label className={styles.search}>
            <span className="mx-visually-hidden">Search reports</span>
            <Search aria-hidden="true" />
            <input
              type="search"
              placeholder="Search the fault description"
              value={filters.search}
              onChange={(event) => handleFilterChange('search', event.target.value)}
              autoComplete="off"
            />
          </label>
          <DateRange
            label="Reported"
            from={filters.dateFrom}
            to={filters.dateTo}
            onFromChange={(value) => handleFilterChange('dateFrom', value)}
            onToChange={(value) => handleFilterChange('dateTo', value)}
            invalid={Boolean(dateRangeError)}
          />
          <SelectMenu ariaLabel="Sort reports" inlineLabel="Sort" value={sort} onChange={handleSortChange} options={SORT_OPTIONS} />
          {hasFilters ? (
            <MxButton variant="ghost" icon={X} onClick={handleClear}>
              Clear
            </MxButton>
          ) : null}
        </div>

        <p className={dateRangeError ? styles.hintError : styles.hint} role={dateRangeError ? 'alert' : undefined}>
          {dateRangeError ?? 'Dates are UTC days, and both ends are inclusive.'}
        </p>
      </div>

      {isLoading ? <RowsSkeleton /> : null}

      {!isLoading && error ? (
        <Panel>
          <ErrorState title="Could not load reports" message={error.message} />
        </Panel>
      ) : null}

      {!isLoading && !error && data ? (
        data.items.length === 0 ? (
          <Panel>
            <EmptyState
              icon={hasFilters ? SearchX : MessageSquareWarning}
              title={hasFilters ? 'No reports match these filters' : 'No reports yet'}
              body={
                hasFilters
                  ? 'Try a different search or date range, or clear the filters to see the whole queue.'
                  : 'Reports appear here as soon as someone files one from the phone app.'
              }
              action={hasFilters ? <MxButton onClick={handleClear}>Clear filters</MxButton> : null}
            />
          </Panel>
        ) : (
          <>
            <ReportRows reports={data.items} />
            <Pager
              page={data.page}
              pageSize={data.pageSize}
              totalPages={data.totalPages}
              totalCount={data.totalCount}
              onPageChange={setPage}
              noun={data.totalCount === 1 ? 'report' : 'reports'}
            />
          </>
        )
      ) : null}
    </section>
  );
}

export default ReportsPage;
