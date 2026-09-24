import { WORK_ORDER_STATUSES, enumLabel } from '../services/workOrdersApi';
import TechnicianSelect from './TechnicianSelect';

/**
 * The search box, the status filter and — for a manager — the technician filter. Every value
 * is controlled by the page; this component only renders them and reports changes upward.
 * Shares the asset catalogue's filter-bar styles, so every list looks like one app.
 *
 * The search is server-side: GET /api/workorders matches the asset tag or the report's
 * description across every page, not just the one on screen. The technician picker is
 * offered only to a manager, because only a manager sees more than one technician's orders —
 * a Technician's board is their own queue, whatever a filter says.
 */
export function WorkOrderFilters({ values, onChange, onClear, showTechnicianFilter }) {
  const hasFilters = Boolean(values.search || values.status || values.technicianId);

  return (
    <div className="asset-filters report-filters">
      <div className="asset-filters__search">
        <label htmlFor="work-order-search" className="visually-hidden">
          Search work orders
        </label>
        <svg className="asset-filters__search-icon" viewBox="0 0 20 20" aria-hidden="true">
          <circle cx="8.5" cy="8.5" r="5.5" fill="none" stroke="currentColor" strokeWidth="1.6" />
          <path d="m13 13 4 4" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" />
        </svg>
        <input
          id="work-order-search"
          type="search"
          placeholder="Search by asset tag or fault…"
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
            {WORK_ORDER_STATUSES.map((status) => (
              <option key={status} value={status}>
                {enumLabel(status)}
              </option>
            ))}
          </select>
        </label>

        {showTechnicianFilter ? (
          <label className="asset-filters__select">
            <span>Technician</span>
            <TechnicianSelect
              value={values.technicianId}
              onChange={(value) => onChange('technicianId', value)}
              emptyLabel="Anyone"
            />
          </label>
        ) : null}

        <button
          type="button"
          className="asset-filters__clear"
          onClick={onClear}
          disabled={!hasFilters}
        >
          Clear
        </button>
      </div>

      <p className="report-filters__hint">
        Search matches the asset tag or the fault as reported, across every page.
      </p>
    </div>
  );
}

export default WorkOrderFilters;
