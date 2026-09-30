import clsx from 'clsx';
import { RotateCcw } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import DateRange from '../../../components/ui/DateRange';
import PageHeader from '../../../components/ui/PageHeader';
import Skeleton from '../../../components/ui/Skeleton';
import { formatDateOnly } from '../../assets/services/assetsApi';
import CategoryReopenTable from '../components/CategoryReopenTable';
import ClarificationStats from '../components/ClarificationStats';
import MetricsPanel from '../components/MetricsPanel';
import ReopenTrendChart from '../components/ReopenTrendChart';
import RepeatFailureTable from '../components/RepeatFailureTable';
import useMetrics from '../hooks/useMetrics';
import { describeNoQuestionCount, formatPercent } from '../services/verificationApi';
import styles from '../verification.module.css';

const EMPTY_RANGE = { fromDate: '', toDate: '' };

function Headline({ label, value, detail, tone }) {
  return (
    <div className={clsx(styles.headline, tone && styles[`headline-${tone}`])}>
      <span className={styles.headlineLabel}>{label}</span>
      <span className={styles.headlineValue}>{value}</span>
      <span className={styles.headlineDetail}>{detail}</span>
    </div>
  );
}

/**
 * The estate's three numbers, from GET /api/analytics/metrics: do repairs hold, is the
 * clarifier asking good questions, and which machines keep failing.
 *
 * EVERY FIGURE ON THIS PAGE IS THE API'S. Nothing here divides, averages, sums money or
 * compares a date with today — the page formats and draws, and that is all. One request feeds
 * every panel, and each renders its own loading, empty and error state from it.
 */
export function MetricsPage() {
  const [range, setRange] = useState(EMPTY_RANGE);

  // The API refuses a range that ends before it starts, so an inverted one is not sent —
  // the panels keep showing all time and the hint says why.
  const rangeError =
    range.fromDate && range.toDate && range.fromDate > range.toDate ? '“From” is after “To”. Showing all time until the range is fixed.' : null;

  const { data, isLoading, error } = useMetrics(rangeError ? EMPTY_RANGE : range);

  function handleRangeChange(name, value) {
    setRange((current) => ({ ...current, [name]: value }));
  }

  const reopen = data?.reopenRate;
  const clarification = data?.clarification;
  const trendIsEmpty = !reopen || reopen.monthlyTrend.every((month) => month.answered === 0);
  const hasRange = Boolean(range.fromDate || range.toDate);

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Insight' }, { label: 'Metrics' }]}
        title="Metrics"
        lead="Whether repairs hold, how well the clarifier asks, and which machines keep failing. Every figure is counted by the API; none is estimated by a model."
        actions={
          <>
            <DateRange
              label="Range"
              from={range.fromDate}
              to={range.toDate}
              onFromChange={(value) => handleRangeChange('fromDate', value)}
              onToChange={(value) => handleRangeChange('toDate', value)}
              invalid={Boolean(rangeError)}
            />
            <MxButton variant="ghost" icon={RotateCcw} onClick={() => setRange(EMPTY_RANGE)} disabled={!hasRange}>
              All time
            </MxButton>
          </>
        }
      />

      <p className={rangeError ? styles.hintError : styles.rangeHint} role={rangeError ? 'alert' : undefined}>
        {rangeError ?? `${hasRange ? 'Showing the chosen range' : 'Showing all time'} · UTC days, both ends included. Checks count by when they fell due, reports by when they were filed.`}
      </p>

      {/* The three numbers first, each beside the counts it came from. */}
      <div className={styles.headlines}>
        {isLoading ? (
          [0, 1, 2].map((index) => <Skeleton key={index} height={132} radius={20} />)
        ) : error || !data ? null : (
          <>
            <Headline
              label="Repairs reopened"
              value={reopen.answered === 0 ? '—' : formatPercent(reopen.reopenRate)}
              detail={reopen.answered === 0 ? 'No answered checks in range' : `${reopen.reopened} of ${reopen.answered} answered checks`}
              tone="red"
            />
            <Headline
              label="Reports that needed no questions"
              value={clarification.reportsClarified === 0 ? '—' : formatPercent(clarification.noQuestionRate)}
              detail={describeNoQuestionCount(clarification)}
              tone="violet"
            />
            <Headline
              label="Machines failing repeatedly"
              value={data.repeatFailures.length}
              detail={`3+ visits in the 90 days to ${formatDateOnly(data.repeatFailuresAsOf)}`}
              tone="amber"
            />
          </>
        )}
      </div>

      <div className={styles.metricsGrid}>
        <MetricsPanel
          title="Reopen rate"
          lead={
            reopen && !trendIsEmpty
              ? `Reopened checks as a share of those a reporter answered, by month. Only answered checks count; silence is in neither column.`
              : 'Reopened checks as a share of the checks a reporter answered, by month.'
          }
          isLoading={isLoading}
          error={error}
          isEmpty={trendIsEmpty}
          emptyBody="No reporter has answered a verification check in these six months. The trend appears once repairs start being confirmed or reopened."
          skeletonHeight={300}
        >
          <ReopenTrendChart monthlyTrend={reopen?.monthlyTrend} />
          {reopen?.byCategory.length ? <CategoryReopenTable categories={reopen.byCategory} /> : null}
        </MetricsPanel>

        <MetricsPanel
          title="Clarification efficiency"
          lead="Fewer questions, more of them answered. Counted over reports filed in the range."
          isLoading={isLoading}
          error={error}
          isEmpty={!clarification || clarification.reportsClarified === 0}
          emptyBody="The clarifier has not run successfully on any report in this range yet."
          skeletonHeight={200}
        >
          <ClarificationStats clarification={clarification} />
        </MetricsPanel>
      </div>

      <MetricsPanel
        title="Repeat failures"
        lead={
          data
            ? `Three or more service visits in the 90 days to ${formatDateOnly(data.repeatFailuresAsOf)}, ranked by cost.`
            : 'Three or more service visits in 90 days, ranked by cost.'
        }
        isLoading={isLoading}
        error={error}
        isEmpty={!data || data.repeatFailures.length === 0}
        emptyBody={
          data
            ? `No asset has three or more service visits in the 90 days to ${formatDateOnly(data.repeatFailuresAsOf)}.`
            : 'No asset has three or more service visits in the window.'
        }
        skeletonHeight={220}
      >
        <RepeatFailureTable failures={data?.repeatFailures ?? []} />
      </MetricsPanel>
    </section>
  );
}

export default MetricsPage;
