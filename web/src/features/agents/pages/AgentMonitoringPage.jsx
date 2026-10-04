import clsx from 'clsx';
import { RotateCcw } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import DateRange from '../../../components/ui/DateRange';
import PageHeader from '../../../components/ui/PageHeader';
import Skeleton from '../../../components/ui/Skeleton';
import MetricsPanel from '../../verification/components/MetricsPanel';
import { formatPercent } from '../../verification/services/verificationApi';
import styles from '../agents.module.css';
import AgentTable from '../components/AgentTable';
import DailyTokensChart from '../components/DailyTokensChart';
import RunList from '../components/RunList';
import useAgentMetrics from '../hooks/useAgentMetrics';
import { describeCost, formatDurationMs, formatTokens, formatUsd, ofRuns } from '../services/agentMetricsApi';

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

/** "Priced at $0.30 in / $2.50 out per million tokens", or why nothing is costed. */
function pricingNote(pricing) {
  if (!pricing?.configured) {
    return 'No LLM price is configured, so tokens are shown without a cost.';
  }
  return `Cost is an estimate at ${formatUsd(pricing.inputPricePerMillionTokensUsd)} in / ${formatUsd(
    pricing.outputPricePerMillionTokensUsd,
  )} out per million tokens, from the tokens the provider reported.`;
}

/**
 * Agent monitoring, from GET /api/analytics/agents: how often each of the five agents runs,
 * fails and retries, how long it takes, and how many tokens it spends — the per-agent view a
 * tracing tool would give, read off the AgentStep rows the API already keeps. Each run links
 * to its workflow, whose audit trail is the full trace.
 *
 * EVERY FIGURE ON THIS PAGE IS THE API'S. Nothing here divides, sums, ranks or costs anything;
 * the page formats and draws. One request feeds every panel, and each renders its own loading,
 * empty and error state from it.
 */
export function AgentMonitoringPage() {
  const [range, setRange] = useState(EMPTY_RANGE);

  const rangeError =
    range.fromDate && range.toDate && range.fromDate > range.toDate
      ? '“From” is after “To”. Showing all time until the range is fixed.'
      : null;

  const { data, isLoading, error } = useAgentMetrics(rangeError ? EMPTY_RANGE : range);

  function handleRangeChange(name, value) {
    setRange((current) => ({ ...current, [name]: value }));
  }

  const totals = data?.totals;
  const pricing = data?.pricing;
  const noRuns = !totals || totals.runs === 0;
  const hasRange = Boolean(range.fromDate || range.toDate);

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Insight' }, { label: 'Agent monitoring' }]}
        title="Agent monitoring"
        lead="How the five agents are running: failures, retries, latency and the tokens the LLM provider reported. Every figure is counted by the API from the recorded agent steps."
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
        {rangeError ??
          `${hasRange ? 'Showing the chosen range' : 'Showing all time'} · UTC days, both ends included, by when each agent ran.`}
      </p>

      <div className={styles.headlines}>
        {isLoading ? (
          [0, 1, 2, 3].map((index) => <Skeleton key={index} height={132} radius={20} />)
        ) : error || !data ? null : (
          <>
            <Headline label="Agent runs" value={totals.runs} detail={`${totals.succeeded} succeeded`} tone="blue" />
            <Headline
              label="Failed"
              value={noRuns ? '—' : formatPercent(totals.failureRate)}
              detail={ofRuns(totals.failed, totals.runs)}
              tone="red"
            />
            <Headline
              label="Median latency"
              value={formatDurationMs(totals.medianDurationMs)}
              detail={
                totals.timedRuns === 0 ? 'No timed runs in range' : `p95 ${formatDurationMs(totals.p95DurationMs)} · ${totals.timedRuns} timed`
              }
              tone="violet"
            />
            <Headline
              label="Tokens reported"
              value={formatTokens(totals.totalTokens)}
              detail={`${describeCost(totals.estimatedCostUsd, pricing, totals.runsWithUsage)} · ${ofRuns(
                totals.runsWithUsage,
                totals.runs,
                'reported',
              )}`}
              tone="amber"
            />
          </>
        )}
      </div>

      <MetricsPanel
        title="Per agent"
        lead={data ? pricingNote(pricing) : 'Runs, failures, retries, latency and tokens for each agent.'}
        isLoading={isLoading}
        error={error}
        isEmpty={noRuns}
        emptyBody="No agent has run in this range. Figures appear once reports are filed and the agents run."
        skeletonHeight={260}
      >
        <AgentTable agents={data?.agents ?? []} pricing={pricing} />
      </MetricsPanel>

      <div className={styles.grid}>
        <MetricsPanel
          title="Tokens per day"
          lead="The last 30 days of the range. A day whose runs reported no usage has no bar — unknown, not zero."
          isLoading={isLoading}
          error={error}
          isEmpty={!data || data.daily.every((day) => day.runsWithUsage === 0)}
          emptyBody="No run in these 30 days reported token usage. Runs in stub mode, and runs recorded before usage was kept, report none."
          skeletonHeight={280}
        >
          <DailyTokensChart daily={data?.daily} />
        </MetricsPanel>

        <MetricsPanel
          title="Slowest runs"
          lead="The agent's own time, slowest first. A call that failed is left out: its time is the timeout's."
          isLoading={isLoading}
          error={error}
          isEmpty={!data || data.slowestRuns.length === 0}
          emptyBody="No run in this range was timed on its own."
          skeletonHeight={280}
        >
          <RunList runs={data?.slowestRuns ?? []} pricing={pricing} />
        </MetricsPanel>
      </div>

      <MetricsPanel
        title="Most tokens"
        lead="The runs that reported the most tokens. Open one to read its whole trace."
        isLoading={isLoading}
        error={error}
        isEmpty={!data || data.costliestRuns.length === 0}
        emptyBody="No run in this range reported token usage. Runs in stub mode, and runs from before usage was recorded, report none."
        skeletonHeight={240}
      >
        <RunList runs={data?.costliestRuns ?? []} pricing={pricing} />
      </MetricsPanel>
    </section>
  );
}

export default AgentMonitoringPage;
