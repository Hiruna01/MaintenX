import clsx from 'clsx';
import { Repeat } from 'lucide-react';

import { Panel, Well } from '../../../components/ui/Panel';
import { Pill, StatusPill } from '../../../components/ui/Pill';
import { formatDateOnly } from '../services/assetsApi';
import styles from '../assets.module.css';

function Metric({ label, value, detail, tone }) {
  return (
    <div className={clsx(styles.metric, tone && styles[`metric-${tone}`])}>
      <dt className={styles.metricLabel}>{label}</dt>
      <dd className={styles.metricValue}>{value}</dd>
      {detail ? <dd className={styles.metricDetail}>{detail}</dd> : null}
    </div>
  );
}

/**
 * The failure summary exactly as the API computed it — counts and date comparisons done in
 * C# over the service history. Nothing here is derived; the footnote says so.
 */
export function SummaryCard({ summary, compact = false }) {
  // Null is not zero: a machine nobody has ever touched is not a machine serviced today.
  const neverServiced = summary.lastServicedOn === null;

  return (
    <Panel
      eyebrow="Failure summary"
      className={compact ? styles.summaryCompact : undefined}
      actions={summary.isRepeatFailure ? <Pill tone="red" icon={Repeat}>Repeat failure</Pill> : null}
    >
      {summary.isRepeatFailure ? (
        <p className={styles.summaryAlert}>
          Three or more service visits in the last 90 days. The pattern is in the notes, not on
          the asset record — read the history in order.
        </p>
      ) : null}

      <Well>
        <dl className={styles.metrics}>
          <Metric
            label="Visits · 90 days"
            value={summary.failureCount3Months}
            tone={summary.isRepeatFailure ? 'red' : undefined}
          />
          <Metric label="Visits · 12 months" value={summary.failureCount12Months} />
          <Metric
            label="Temporary fixes"
            value={summary.temporaryFixCount}
            detail="Whole history"
            tone={summary.temporaryFixCount > 0 ? 'amber' : undefined}
          />
          <Metric
            label="Last serviced"
            value={neverServiced ? 'Never' : formatDateOnly(summary.lastServicedOn, { day: 'numeric', month: 'short' })}
            detail={
              neverServiced
                ? 'No visit on record'
                : `${summary.daysSinceLastService} ${summary.daysSinceLastService === 1 ? 'day' : 'days'} ago`
            }
          />
        </dl>
      </Well>

      <div className={styles.outcomes}>
        <span className={styles.outcomesLabel}>Outcomes on record</span>
        {summary.distinctOutcomes.length === 0 ? (
          <span className={styles.muted}>None yet</span>
        ) : (
          <ul aria-label="Outcomes on record">
            {summary.distinctOutcomes.map((outcome) => (
              <li key={outcome}>
                <StatusPill status={outcome} />
              </li>
            ))}
          </ul>
        )}
      </div>

      {compact ? null : (
        <p className={styles.footnote}>
          Counted by the API from the service history — not an agent&apos;s assessment.
          &ldquo;90 days&rdquo; means exactly 90.
        </p>
      )}
    </Panel>
  );
}

export default SummaryCard;
