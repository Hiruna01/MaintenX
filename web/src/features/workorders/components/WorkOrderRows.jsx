import { ChevronRight, UserRound } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';

import { formatInstant, initials, timeAgo } from '../../../components/ui/format';
import { StatusPill } from '../../../components/ui/Pill';
import rows from '../../../components/ui/rows.module.css';
import TagChip from '../../assets/components/TagChip';
import { enumLabel, formatMoney } from '../services/workOrdersApi';
import styles from '../workorders.module.css';
import SlaPill from './SlaPill';
import StrategyPill from './StrategyPill';

/** The dispatch board's rows. Presentational only — it receives the page and fetches nothing. */
export function WorkOrderRows({ workOrders }) {
  const navigate = useNavigate();

  return (
    <div className={rows.wrap}>
      <table className={rows.table}>
        <caption className="mx-visually-hidden">Work orders</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Asset</th>
            <th scope="col">Status</th>
            <th scope="col">Technician</th>
            <th scope="col" style={{ textAlign: 'right' }}>
              Estimate
            </th>
            <th scope="col">Raised</th>
            <th scope="col">
              <span className="mx-visually-hidden">Open</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {workOrders.map((order, index) => (
            <tr
              key={order.id}
              className={rows.row}
              style={{ animationDelay: `${Math.min(index, 10) * 28}ms` }}
              onClick={(event) => {
                if (event.target.closest('a')) return;
                navigate(`/workorders/${order.id}`);
              }}
            >
              <td className={rows.id}>#{order.id}</td>
              <td className={rows.main}>
                <Link to={`/workorders/${order.id}`} className={styles.chipLink} aria-label={`Work order ${order.id}, ${order.assetTag}`}>
                  <TagChip tag={order.assetTag} />
                </Link>
                <span className={rows.sub}>
                  <StrategyPill strategy={order.strategy} />
                  <Link to={`/reports/${order.reportId}`}>Report #{order.reportId}</Link>
                </span>
              </td>
              <td>
                <span className={rows.stack}>
                  <StatusPill status={order.status} label={enumLabel(order.status)} />
                  <SlaPill sla={order.sla} dueAt={order.dueAt} />
                </span>
              </td>
              <td>
                {order.assignedTechnicianName ? (
                  <span className={styles.person}>
                    <span className={styles.personAvatar} aria-hidden="true">
                      {initials(order.assignedTechnicianName)}
                    </span>
                    {order.assignedTechnicianName}
                  </span>
                ) : (
                  <span className={styles.unassigned}>
                    <UserRound aria-hidden="true" />
                    Unassigned
                  </span>
                )}
              </td>
              <td style={{ textAlign: 'right' }}>
                <span className={styles.money}>{formatMoney(order.estimatedCost)}</span>
                {order.actualCost !== null && order.actualCost !== undefined ? (
                  <span className={rows.timeSub}>Actual {formatMoney(order.actualCost)}</span>
                ) : null}
              </td>
              <td>
                <span className={rows.time}>{formatInstant(order.createdAt)}</span>
                <span className={rows.timeSub}>{timeAgo(order.createdAt)}</span>
              </td>
              <td className={rows.chevron} aria-hidden="true">
                <ChevronRight />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default WorkOrderRows;
