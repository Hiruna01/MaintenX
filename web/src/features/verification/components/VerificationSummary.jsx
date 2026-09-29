import clsx from 'clsx';

import Skeleton from '../../../components/ui/Skeleton';
import useFetch from '../../../hooks/useFetch';
import { VERIFICATION_SUMMARY_PATH, formatPercent } from '../services/verificationApi';
import styles from '../verification.module.css';

function Tile({ label, value, detail, tone }) {
  return (
    <div className={clsx(styles.kpi, tone && styles[`kpi-${tone}`])}>
      <span className={styles.kpiLabel}>{label}</span>
      <span className={styles.kpiValue}>{value}</span>
      <span className={styles.kpiDetail}>{detail}</span>
    </div>
  );
}

/**
 * The verification loop at a glance, for a facilities manager — every figure from
 * GET /api/analytics/verification. The confirmation rate is over ANSWERED checks only
 * (Confirmed + Reopened), as the API computes it; silence is in neither column.
 * A failed read says so in one line rather than taking the list down with it.
 */
export function VerificationSummary() {
  const { data, isLoading, error } = useFetch(VERIFICATION_SUMMARY_PATH);

  if (isLoading) {
    return (
      <div className={styles.kpis} role="status" aria-label="Loading the verification summary">
        {[0, 1, 2, 3].map((index) => (
          <Skeleton key={index} height={96} radius={16} />
        ))}
      </div>
    );
  }

  if (error || !data) {
    return <p className={styles.summaryError}>The verification summary could not be loaded. The list below is unaffected.</p>;
  }

  const answered = data.confirmed + data.reopened;
  const days = data.averageDaysToRespond;

  return (
    <div className={styles.kpis}>
      <Tile
        label="Repairs that held"
        value={answered === 0 ? '—' : formatPercent(data.confirmationRate)}
        detail={answered === 0 ? 'Nobody has answered a check yet' : `${data.confirmed} of ${answered} answered`}
        tone="green"
      />
      <Tile
        label="Reopened"
        value={answered === 0 ? '—' : formatPercent(data.reopenRate)}
        detail={`${data.reopened} reopened · ${data.escalated} escalated`}
        tone="red"
      />
      <Tile
        label="Waiting on a reporter"
        value={data.awaitingReporterResponse}
        detail={`${data.pending} still inside their waiting period`}
        tone="amber"
      />
      <Tile
        label="Average reply"
        value={days === null || days === undefined ? '—' : `${Number(days).toFixed(1)} d`}
        detail={`${data.overdueUnprocessed} overdue and not yet asked`}
      />
    </div>
  );
}

export default VerificationSummary;
