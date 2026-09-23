import { REPORT_STATUSES, enumLabel } from '../services/reportsApi';

/**
 * The search box, the status filter and the date range. Every value is controlled by the
 * page; this component only renders them and reports changes upward.
 *
 * Shares the asset catalogue's filter-bar styles, so the two lists look like one app.
 *
 * The search is server-side — GET /api/reports matches the description across every page —
 * and the dates are UTC calendar days with both ends inclusive, which is how the API
 * compares them against CreatedAt. The hint says so rather than implying local days.
 */
export function ReportFilters({ values, onChange, onClear, dateRangeError }) {
  const hasFilters = Boolean(values.search || values.status || values.dateFrom || values.dateTo);

  return (
    <div className="asset-filters report-filters">
      <div className="asset-filters__search">
        <label htmlFor="report-search" className="visually-hidden">
          Search reports
        </label>
        <svg className="asset-filters__search-icon" viewBox="0 0 20 20" aria-hidden="true">
          <circle cx="8.5" cy="8.5" r="5.5" fill="none" stroke="currentColor" strokeWidth="1.6" />
          <path d="m13 13 4 4" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
        </svg>
        <input
          id="report-search"
          type="search"
          placeholder="Search the description…"
          value={values.search}
          onChange={(event) => onChange('search', event.target.value)}
          autoComplete="off"
        />
      </div>

      <div className="asset-filters__selects">
        <label className="asset-filters__select">
          <span>Status</span>
          <select value={values.status} onChange={(event) => onChange('status', event.target.value)}>
            <option value="">Any status</option>
            {REPORT_STATUSES.map((status) => (
              <option key={status} value={status}>
                {enumLabel(status)}
              </option>
            ))}
          </select>
        </label>

        <label className="asset-filters__select">
          <span>From</span>
          <input
            type="date"
            value={values.dateFrom}
            max={values.dateTo || undefined}
            onChange={(event) => onChange('dateFrom', event.target.value)}
            aria-invalid={dateRangeError ? 'true' : undefined}
          />
        </label>

        <label className="asset-filters__select">
          <span>To</span>
          <input
            type="date"
            value={values.dateTo}
            min={values.dateFrom || undefined}
            onChange={(event) => onChange('dateTo', event.target.value)}
            aria-invalid={dateRangeError ? 'true' : undefined}
          />
        </label>

        <button
          type="button"
          className="asset-filters__clear"
          onClick={onClear}
          disabled={!hasFilters}
        >
          Clear
        </button>
      </div>

      <p className={`report-filters__hint ${dateRangeError ? 'report-filters__hint--error' : ''}`.trim()}>
        {dateRangeError ??
          'Search matches the description across every page. Dates are UTC days and include both ends.'}
      </p>
    </div>
  );
}

export default ReportFilters;
