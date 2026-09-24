import { Link } from 'react-router-dom';

import StrategyBadge from '../../workorders/components/StrategyBadge';
import WorkOrderStatusBadge from '../../workorders/components/WorkOrderStatusBadge';
import { formatDateTime } from '../services/verificationApi';

/**
 * What a failed repair looped back to: the original report, and the work orders raised on the
 * same asset since this repair was completed.
 *
 * `followUpWorkOrders` is null for a Reporter — they read no work orders anywhere — and empty
 * for a manager when nothing has been raised yet. The two are said differently: "none yet" is
 * a gap the manager may need to close, "not shown" is a permission.
 */
export function LoopBackPanel({ check }) {
  const followUps = check.followUpWorkOrders;

  return (
    <div className="work-order-panel loop-back">
      <p className="approval-panel__label">Back to the fault</p>
      <p className="work-order-panel__value">
        <Link to={`/reports/${check.reportId}`}>Report #{check.reportId}</Link> — the fault this
        repair was meant to fix.
      </p>

      <p className="approval-panel__label">Follow-up work</p>
      {followUps === null || followUps === undefined ? (
        <p className="work-order-panel__note">
          The facilities team sees the follow-up work for this asset. It is not shown on a
          reporter&apos;s account.
        </p>
      ) : followUps.length === 0 ? (
        <p className="work-order-panel__note loop-back__gap">
          No work order has been raised on this asset since the repair. The fault is back, and
          nothing is scheduled to fix it yet.
        </p>
      ) : (
        <ol className="related-list">
          {followUps.map((order) => (
            <li key={order.id} className="related-list__item">
              <div className="related-list__head">
                <Link to={`/workorders/${order.id}`}>Work order #{order.id}</Link>
                <WorkOrderStatusBadge status={order.status} />
                <StrategyBadge strategy={order.strategy} />
                <span className="related-list__when">Raised {formatDateTime(order.createdAt)}</span>
              </div>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

export default LoopBackPanel;
