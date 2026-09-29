import { Repeat } from 'lucide-react';

import MxButton from '../../../components/ui/Button';
import { Panel, Well } from '../../../components/ui/Panel';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import TagChip from '../../assets/components/TagChip';
import { formatDateOnly } from '../../assets/services/assetsApi';
import { formatPercent } from '../../verification/services/verificationApi';
import { formatMoney } from '../../workorders/services/workOrdersApi';
import styles from '../dashboard.module.css';
import { LinkRow, ListSkeleton } from './parts';

function Stat({ label, value, detail }) {
  return (
    <div className={styles.stat}>
      <dt>{label}</dt>
      <dd className={styles.statValue}>{value}</dd>
      <dd className={styles.statDetail}>{detail}</dd>
    </div>
  );
}

/**
 * Four figures from GET /api/analytics/metrics, each beside the counts it came from so "0%
 * of nothing" reads differently from 0%. Every number is the API's; a null median is "—".
 */
export function KeyMetricsPanel({ metrics }) {
  const { data, isLoading, error } = metrics;

  return (
    <Panel
      eyebrow="Key metrics"
      actions={
        <MxButton size="sm" variant="ghost" to="/metrics">
          All metrics
        </MxButton>
      }
    >
      {isLoading ? <Skeleton height={176} radius={14} /> : null}
      {!isLoading && error ? <ErrorState compact title="Could not load metrics" message={error.message} /> : null}
      {!isLoading && !error && data ? (
        <Well>
          <dl className={styles.stats}>
            <Stat
              label="Reopen rate"
              value={formatPercent(data.reopenRate.reopenRate)}
              detail={`${data.reopenRate.reopened} of ${data.reopenRate.answered} answered checks`}
            />
            <Stat
              label="Needed no questions"
              value={formatPercent(data.clarification.noQuestionRate)}
              detail={`${data.clarification.reportsWithNoQuestions} of ${data.clarification.reportsClarified} reports`}
            />
            <Stat
              label="Questions answered"
              value={formatPercent(data.clarification.answerRate)}
              detail={`${data.clarification.questionsAnswered} of ${data.clarification.questionsAsked} asked`}
            />
            <Stat
              label="Median time to answer"
              value={
                data.clarification.medianHoursToAnswer === null
                  ? '—'
                  : `${Number(data.clarification.medianHoursToAnswer).toFixed(1)} h`
              }
              detail="Reporter replies"
            />
          </dl>
        </Well>
      ) : null}
    </Panel>
  );
}

/** The API's repeat-failure list, ranked by cost as it arrives. */
export function RepeatFailuresPanel({ metrics }) {
  const { data, isLoading, error } = metrics;
  const assets = data?.repeatFailures ?? [];

  return (
    <Panel eyebrow="Repeat failures" count={data ? assets.length : null}>
      {isLoading ? <ListSkeleton rows={2} /> : null}
      {!isLoading && error ? <ErrorState compact title="Could not load repeat failures" message={error.message} /> : null}
      {!isLoading && !error && data && assets.length === 0 ? (
        <EmptyState compact icon={Repeat} title="No repeat failures" body="No asset has three or more visits in the last 90 days." />
      ) : null}
      {!isLoading && !error && assets.length > 0 ? (
        <>
          <ul className={styles.list}>
            {assets.slice(0, 3).map((asset) => (
              <LinkRow
                key={asset.assetId}
                to={`/assets/${asset.assetId}`}
                trailing={
                  <>
                    <span className={styles.money}>{formatMoney(asset.totalCost)}</span>
                    {asset.visitsWithoutCost > 0 ? (
                      <span className={styles.subtle}>+{asset.visitsWithoutCost} uncosted</span>
                    ) : null}
                  </>
                }
              >
                <span className={styles.rowTop}>
                  <TagChip tag={asset.assetTag} />
                </span>
                <span className={styles.rowTitle}>{asset.name}</span>
                <span className={styles.rowDesc}>
                  {asset.failureCount} visits in 90 days · last {formatDateOnly(asset.lastServicedOn)}
                </span>
              </LinkRow>
            ))}
          </ul>
          <p className={styles.caption}>As of {formatDateOnly(data.repeatFailuresAsOf)} · counted by the API</p>
        </>
      ) : null}
    </Panel>
  );
}
