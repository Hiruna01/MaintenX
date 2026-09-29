import { FileWarning, RotateCcw } from 'lucide-react';
import { Link } from 'react-router-dom';

import { formatInstant } from '../../../components/ui/format';
import { Panel } from '../../../components/ui/Panel';
import { StatusPill } from '../../../components/ui/Pill';
import { EmptyState } from '../../../components/ui/States';
import StrategyPill from '../../workorders/components/StrategyPill';
import { enumLabel } from '../../workorders/services/workOrdersApi';
import styles from '../verification.module.css';

/**
 * Reports filed against the same asset after the repair was completed — somebody seeing the
 * fault again. Chosen by the API; this only lists them, oldest first, each description
 * verbatim. A manager's view only: a Reporter's copy carries null and the page leaves the
 * panel out rather than say "none".
 */
export function NewReportsPanel({ reports }) {
  return (
    <Panel eyebrow="Reported since the repair" count={reports.length || null}>
      <p className={styles.sectionLead}>Faults filed against this asset after the work order was completed.</p>
      {reports.length === 0 ? (
        <EmptyState compact icon={FileWarning} title="Nothing new" body="Nobody has reported a fault on this asset since the repair was completed." />
      ) : (
        <ol className={styles.related}>
          {reports.map((report) => (
            <li key={report.id}>
              <div className={styles.relatedHead}>
                <Link to={`/reports/${report.id}`}>Report #{report.id}</Link>
                <StatusPill status={report.status} />
                <span className={styles.relatedWhen}>{formatInstant(report.createdAt)}</span>
              </div>
              <p className={styles.relatedText}>{report.description}</p>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  );
}

/**
 * What a failed repair looped back to: the original report, and the work orders raised on the
 * same asset since. `followUpWorkOrders` is null for a Reporter (they read no work orders) and
 * empty for a manager when nothing has been raised — "not shown" is a permission, "none yet"
 * is a gap to close, and they are said differently.
 */
export function LoopBackPanel({ check }) {
  const followUps = check.followUpWorkOrders;

  return (
    <Panel eyebrow="Where it went next" className={styles.loopPanel}>
      <p className={styles.loopLead}>
        <RotateCcw aria-hidden="true" />
        <span>
          The repair did not hold. Back to the fault: <Link to={`/reports/${check.reportId}`}>report #{check.reportId}</Link>.
        </span>
      </p>

      <p className={styles.evidenceLabel}>Follow-up work</p>
      {followUps === null || followUps === undefined ? (
        <p className={styles.voiceEmpty}>The facilities team sees the follow-up work for this asset. It is not shown on a reporter&apos;s account.</p>
      ) : followUps.length === 0 ? (
        <p className={styles.gap}>No work order has been raised on this asset since the repair. The fault is back, and nothing is scheduled to fix it yet.</p>
      ) : (
        <ol className={styles.related}>
          {followUps.map((order) => (
            <li key={order.id}>
              <div className={styles.relatedHead}>
                <Link to={`/workorders/${order.id}`}>Work order #{order.id}</Link>
                <StatusPill status={order.status} label={enumLabel(order.status)} />
                <StrategyPill strategy={order.strategy} />
                <span className={styles.relatedWhen}>Raised {formatInstant(order.createdAt)}</span>
              </div>
            </li>
          ))}
        </ol>
      )}
    </Panel>
  );
}
