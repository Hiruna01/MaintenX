import { Link } from 'react-router-dom';

import { VERIFICATION_SORTS, formatDateTime } from '../services/verificationApi';
import OverdueFlag from './OverdueFlag';
import VerificationStatusBadge from './VerificationStatusBadge';

/**
 * A column header that sorts. Only Due and Status sort because those are the two members of
 * VerificationSort; the API takes no direction, so there is nothing to toggle.
 */
function SortableHeader({ label, sortKey, direction, currentSort, onSortChange }) {
  const isActive = currentSort === sortKey;

  return (
    <th scope="col" aria-sort={isActive ? direction : 'none'}>
      <button
        type="button"
        className={`sort-button ${isActive ? 'sort-button--active' : ''}`.trim()}
        onClick={() => onSortChange(sortKey)}
        title={direction === 'descending' ? 'Latest due first' : 'Grouped by status, A–Z'}
      >
        {label}
        <svg className="sort-button__icon" viewBox="0 0 12 12" aria-hidden="true">
          <path d="M6 2.5v7M3 6.5l3 3 3-3" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
        </svg>
      </button>
    </th>
  );
}

/** The reporter's answer in words. Null is "not answered", never "no". */
function answerLabel(confirmed) {
  if (confirmed === true) return 'Yes, fixed';
  if (confirmed === false) return 'No, not fixed';
  return 'Not answered';
}

/**
 * Presentational only — it receives the page of checks and fetches nothing. Shares the asset
 * catalogue's table styles. An overdue row is marked by the API's `isOverdue`, as a flag in the
 * status cell and a coloured edge on the row, so it reads at a glance without relying on colour.
 */
export function VerificationTable({ checks, sort, onSortChange }) {
  return (
    <div className="asset-table">
      <table>
        <caption className="visually-hidden">Verification checks</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Asset</th>
            <SortableHeader
              label="Status"
              sortKey={VERIFICATION_SORTS.Status}
              direction="ascending"
              currentSort={sort}
              onSortChange={onSortChange}
            />
            <th scope="col">Reporter says</th>
            <SortableHeader
              label="Due"
              sortKey={VERIFICATION_SORTS.DueAt}
              direction="descending"
              currentSort={sort}
              onSortChange={onSortChange}
            />
          </tr>
        </thead>
        <tbody>
          {checks.map((check) => (
            <tr key={check.id} className={check.isOverdue ? 'verification-row--overdue' : undefined}>
              <td className="report-table__id">
                <Link to={`/verifications/${check.id}`}>{check.id}</Link>
              </td>
              <td>
                <Link className="asset-table__name" to={`/verifications/${check.id}`}>
                  <span className="asset-tag">{check.assetTag}</span>
                </Link>
                <span className="asset-table__sub">Work order #{check.workOrderId}</span>
              </td>
              <td>
                <div className="report-table__status">
                  <VerificationStatusBadge status={check.status} />
                  {check.isOverdue ? <OverdueFlag status={check.status} /> : null}
                </div>
              </td>
              <td>{answerLabel(check.reporterConfirmed)}</td>
              <td className="asset-table__date">{formatDateTime(check.dueAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default VerificationTable;
