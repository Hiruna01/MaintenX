import clsx from 'clsx';
import { Check, X } from 'lucide-react';

import { formatInstant } from '../../../components/ui/format';
import { Panel } from '../../../components/ui/Panel';
import { formatSlot } from '../services/workOrdersApi';
import styles from '../workorders.module.css';

/**
 * Where the job has got to, read off facts already on the order — the status, whether
 * anyone is assigned, whether a visit is booked. It describes; it moves nothing, and every
 * transition is still the API's to allow or refuse.
 */
function stagesFor(order) {
  const stopped = order.status === 'Rejected' || order.status === 'Cancelled';
  const waitingOnDecision = order.status === 'AwaitingApproval' || order.status === 'Draft';
  const assigned = Boolean(order.assignedTechnician);
  const booked = order.scheduledSlots.length > 0;
  const completed = order.status === 'Completed';

  let approval;
  if (order.status === 'Rejected') approval = { state: 'stopped', detail: `Rejected${order.approvedBy ? ` by ${order.approvedBy.fullName}` : ''}` };
  else if (order.status === 'Draft') approval = { state: 'current', detail: order.revisionNote ? 'Sent back for revision' : 'Draft — not yet routed' };
  else if (order.status === 'AwaitingApproval') approval = { state: 'current', detail: 'Waiting for a manager' };
  else if (order.status === 'Cancelled') approval = { state: 'stopped', detail: 'Cancelled' };
  else approval = { state: 'done', detail: order.approvedBy ? `Approved by ${order.approvedBy.fullName}` : 'Approved automatically' };

  const after = (isDone, isNext, doneDetail, nextDetail) => {
    if (stopped) return { state: 'off', detail: '—' };
    if (isDone) return { state: 'done', detail: doneDetail };
    if (isNext && !waitingOnDecision) return { state: 'current', detail: nextDetail };
    return { state: 'waiting', detail: 'Not yet' };
  };

  return [
    { label: 'Raised', state: 'done', detail: formatInstant(order.createdAt) },
    { label: 'Approval', ...approval },
    { label: 'Assigned', ...after(assigned, true, order.assignedTechnician?.fullName, 'Nobody yet') },
    {
      label: 'Visit booked',
      ...after(booked, assigned, booked ? formatSlot(order.scheduledSlots[0]) : '', 'Find a time'),
    },
    { label: 'Completed', ...after(completed, booked, formatInstant(order.completedAt), 'In the diary') },
  ];
}

export function JobProgress({ order }) {
  const stages = stagesFor(order);

  return (
    <Panel eyebrow="Progress">
      <ol className={styles.progress}>
        {stages.map((stage) => (
          <li key={stage.label} className={clsx(styles.progressStep, styles[`progress-${stage.state}`])} aria-current={stage.state === 'current' ? 'step' : undefined}>
            <span className={styles.progressNode} aria-hidden="true">
              {stage.state === 'done' ? <Check strokeWidth={2.4} /> : stage.state === 'stopped' ? <X strokeWidth={2.4} /> : null}
            </span>
            <span className={styles.progressLabel}>{stage.label}</span>
            <span className={styles.progressDetail}>{stage.detail}</span>
          </li>
        ))}
      </ol>
    </Panel>
  );
}

export default JobProgress;
