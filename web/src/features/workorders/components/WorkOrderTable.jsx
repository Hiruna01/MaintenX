import { Link } from 'react-router-dom';

import { WORK_ORDER_SORTS, formatDateTime, formatMoney } from '../services/workOrdersApi';
import StrategyBadge from './StrategyBadge';
import WorkOrderStatusBadge from './WorkOrderStatusBadge';

/**
 * A column header that sorts. Only Estimate and Raised are sortable because those are the
 * two members of WorkOrderSort; a clickable header that sorted only the page already fetched
 * would put a different order on every page and look like a server sort while not being one.
 * The API takes no direction, so there is nothing to toggle — the title says which way it runs.
 */
function SortableHeader({ label, sortKey, title, currentSort, onSortChange, numeric }) {
  const isActive = currentSort === sortKey;

  return (
    <th scope="col" aria-sort={isActive ? 'descending' : 'none'} className={numeric ? 'work-order-table__num' : undefined}>
      <button
        type="button"
        className={`sort-button ${isActive ? 'sort-button--active' : ''}`.trim()}
        onClick={() => onSortChange(sortKey)}
        title={title}
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
 * The dispatch board's rows. Presentational only — it receives the page and fetches nothing.
 * Shares the asset catalogue's table styles.
 */
export function WorkOrderTable({ workOrders, sort, onSortChange }) {
  return (
    <div className="asset-table">
      <table>
        <caption className="visually-hidden">Work orders</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Asset</th>
            <th scope="col">Strategy</th>
            <th scope="col">Status</th>
            <th scope="col">Technician</th>
            <SortableHeader
              label="Estimate"
              sortKey={WORK_ORDER_SORTS.Cost}
              title="Highest estimate first"
              currentSort={sort}
              onSortChange={onSortChange}
              numeric
            />
            <SortableHeader
              label="Raised"
              sortKey={WORK_ORDER_SORTS.CreatedAt}
              title="Newest first"
              currentSort={sort}
              onSortChange={onSortChange}
            />
          </tr>
        </thead>
        <tbody>
          {workOrders.map((order) => (
            <tr key={order.id}>
              <td className="report-table__id">
                <Link to={`/workorders/${order.id}`}>{order.id}</Link>
              </td>
              <td>
                <Link to={`/workorders/${order.id}`} className="asset-tag">
                  {order.assetTag}
                </Link>
                <span className="asset-table__sub">Report #{order.reportId}</span>
              </td>
              <td>
                <StrategyBadge strategy={order.strategy} />
              </td>
              <td>
                <WorkOrderStatusBadge status={order.status} />
              </td>
              <td>
                {order.assignedTechnicianName ?? (
                  <span className="work-order-table__unassigned">Unassigned</span>
                )}
              </td>
              <td className="work-order-table__num">
                {formatMoney(order.estimatedCost)}
                {order.actualCost !== null && order.actualCost !== undefined ? (
                  <span className="asset-table__sub">Actual {formatMoney(order.actualCost)}</span>
                ) : null}
              </td>
              <td className="asset-table__date">{formatDateTime(order.createdAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default WorkOrderTable;
