import { Link } from 'react-router-dom';

import { REPORT_SORTS, formatDateTime } from '../services/reportsApi';
import ReportStatusBadge from './ReportStatusBadge';

/**
 * A column header that sorts. Only Reported and Status are sortable because those are the
 * two members of ReportSort; a clickable header that sorted only the page already fetched
 * would put a different order on every page and look like a server sort while not being one.
 * The API takes no direction, so there is nothing to toggle — the caption on each button says
 * which way it runs.
 */
function SortableHeader({ label, sortKey, direction, currentSort, onSortChange }) {
  const isActive = currentSort === sortKey;

  return (
    <th scope="col" aria-sort={isActive ? direction : 'none'}>
      <button
        type="button"
        className={`sort-button ${isActive ? 'sort-button--active' : ''}`.trim()}
        onClick={() => onSortChange(sortKey)}
        title={direction === 'descending' ? 'Newest first' : 'Grouped by status, A–Z'}
      >
        {label}
        <svg className="sort-button__icon" viewBox="0 0 12 12" aria-hidden="true">
          <path d="M6 2.5v7M3 6.5l3 3 3-3" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>
    </th>
  );
}

/**
 * Presentational only — it receives the page of reports and fetches nothing. Shares the
 * asset catalogue's table styles.
 *
 * The description is clamped to two lines here because this is a worklist row; the detail
 * page shows it in full and verbatim.
 */
export function ReportTable({ reports, sort, onSortChange }) {
  return (
    <div className="asset-table">
      <table>
        <caption className="visually-hidden">Maintenance reports</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Fault</th>
            <th scope="col">Room</th>
            <SortableHeader
              label="Status"
              sortKey={REPORT_SORTS.Status}
              direction="ascending"
              currentSort={sort}
              onSortChange={onSortChange}
            />
            <SortableHeader
              label="Reported"
              sortKey={REPORT_SORTS.CreatedAt}
              direction="descending"
              currentSort={sort}
              onSortChange={onSortChange}
            />
          </tr>
        </thead>
        <tbody>
          {reports.map((report) => (
            <tr key={report.id}>
              <td className="report-table__id">
                <Link to={`/reports/${report.id}`}>{report.id}</Link>
              </td>
              <td>
                <Link className="asset-table__name report-table__description" to={`/reports/${report.id}`}>
                  {report.description}
                </Link>
                {report.assetId ? (
                  <span className="asset-table__sub">Asset #{report.assetId} identified</span>
                ) : null}
              </td>
              <td>{report.roomName}</td>
              <td>
                <div className="report-table__status">
                  <ReportStatusBadge status={report.status} />
                  {/* "This is waiting on the reporter" — the one thing a list needs to say
                      about clarification, as a count the API computed. */}
                  {report.unansweredQuestionCount > 0 ? (
                    <span className="report-table__waiting">
                      {report.unansweredQuestionCount} unanswered
                    </span>
                  ) : null}
                </div>
              </td>
              <td className="asset-table__date">{formatDateTime(report.createdAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default ReportTable;
