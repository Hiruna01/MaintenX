import { enumLabel } from '../services/workOrdersApi';

/**
 * A work order's status as a pill. The modifier class is the enum NAME the API sent
 * ("AwaitingApproval"), never an ordinal, so the colour cannot drift from the meaning.
 */
export function WorkOrderStatusBadge({ status }) {
  return (
    <span className={`work-order-status work-order-status--${status}`}>
      <span className="asset-status__dot" aria-hidden="true" />
      {enumLabel(status)}
    </span>
  );
}

export default WorkOrderStatusBadge;
