import { VERIFICATION_STATUSES, enumLabel } from '../services/verificationApi';

/**
 * The search box, the status filter and the due-date range. Every value is controlled by the
 * page; this component only renders them and reports changes upward. Shares the asset
 * catalogue's filter-bar styles, so the lists look like one app.
 *
 * The search is server-side — GET /api/verifications matches the asset tag across every page —
 * and the dates bound DueAt as UTC days with both ends inclusive. The hint says so.
 */
export function VerificationFilters({ values, onChange, onClear, dateRangeError }) {
  const hasFilters = Boolean(values.search || values.status || values.dateFrom || values.dateTo);

  return (
    <div className="asset-filters report-filters">
      <div className="asset-filters__search">
        <label htmlFor="verification-search" className="visually-hidden">
          Search checks by asset tag
        </label>
        <svg className="asset-filters__search-icon" viewBox="0 0 20 20" aria-hidden="true">
          <circle cx="8.5" cy="8.5" r="5.5" fill="none" stroke="currentColor" strokeWidth="1.6" />
          <path d="m13 13 4 4" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
        </svg>
        <input
          id="verification-search"
          type="search"
          placeholder="Search by asset tag…"
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
            {VERIFICATION_STATUSES.map((status) => (
              <option key={status} value={status}>
                {enumLabel(status)}
              </option>
            ))}
          </select>
        </label>

        <label className="asset-filters__select">
          <span>Due from</span>
          <input
            type="date"
            value={values.dateFrom}
            max={values.dateTo || undefined}
            onChange={(event) => onChange('dateFrom', event.target.value)}
            aria-invalid={dateRangeError ? 'true' : undefined}
          />
        </label>

        <label className="asset-filters__select">
          <span>Due to</span>
          <input
            type="date"
            value={values.dateTo}
            min={values.dateFrom || undefined}
            onChange={(event) => onChange('dateTo', event.target.value)}
            aria-invalid={dateRangeError ? 'true' : undefined}
          />
        </label>

        <button type="button" className="asset-filters__clear" onClick={onClear} disabled={!hasFilters}>
          Clear
        </button>
      </div>

      <p className={`report-filters__hint ${dateRangeError ? 'report-filters__hint--error' : ''}`.trim()}>
        {dateRangeError ??
          'Search matches the asset tag across every page. Dates are when the check falls due — UTC days, both ends included.'}
      </p>
    </div>
  );
}

export default VerificationFilters;
