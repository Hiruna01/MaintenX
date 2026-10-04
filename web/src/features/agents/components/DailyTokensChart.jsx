import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import styles from '../agents.module.css';
import { dailyChartRows, formatTokens, formatUsd } from '../services/agentMetricsApi';

/** The hover text: the day's runs and the tokens they reported — or that they reported none. */
function DayTooltip({ active, payload }) {
  if (!active || !payload?.length) return null;
  const row = payload[0].payload;

  let detail;
  if (row.runs === 0) detail = 'No agent runs';
  else if (row.promptTokens === null) detail = `${row.runs} runs — none reported usage`;
  else {
    detail = `${formatTokens(row.promptTokens)} in · ${formatTokens(row.completionTokens)} out — ${row.runsWithUsage} of ${row.runs} runs reported`;
  }

  return (
    <div className={styles.tooltip}>
      <strong>{row.label}</strong>
      <span>{detail}</span>
      {row.estimatedCostUsd !== null && row.runs > 0 ? <span>Est. {formatUsd(row.estimatedCostUsd)}</span> : null}
    </div>
  );
}

/**
 * Prompt and completion tokens per UTC day, stacked, as the API reported them. A day whose runs
 * reported nothing has no bar (null is unknown, not zero); a day with no runs is a real 0.
 */
export function DailyTokensChart({ daily }) {
  const rows = dailyChartRows(daily);

  return (
    <div className={styles.chart} role="img" aria-label="Tokens reported per day, as a stacked bar chart">
      <ResponsiveContainer width="100%" height={260}>
        <BarChart data={rows} margin={{ top: 12, right: 12, bottom: 0, left: 0 }}>
          <CartesianGrid stroke="#eceef2" vertical={false} />
          <XAxis
            dataKey="label"
            tick={{ fill: '#6a707c', fontSize: 11 }}
            tickLine={false}
            axisLine={{ stroke: '#e3e6eb' }}
            interval="preserveStartEnd"
            minTickGap={16}
          />
          <YAxis
            tickFormatter={(value) => value.toLocaleString()}
            tick={{ fill: '#9aa0aa', fontSize: 11, fontFamily: 'JetBrains Mono, monospace' }}
            tickLine={false}
            axisLine={false}
            width={56}
          />
          <Tooltip content={<DayTooltip />} cursor={{ fill: 'rgba(91, 80, 232, 0.06)' }} />
          <Bar dataKey="promptTokens" name="Prompt" stackId="tokens" fill="#5b50e8" isAnimationActive={false} />
          <Bar
            dataKey="completionTokens"
            name="Completion"
            stackId="tokens"
            fill="#b7b1f5"
            radius={[4, 4, 0, 0]}
            isAnimationActive={false}
          />
        </BarChart>
      </ResponsiveContainer>
      <p className={styles.legend}>
        <span className={styles.legendPrompt} aria-hidden="true" /> Prompt (in)
        <span className={styles.legendCompletion} aria-hidden="true" /> Completion (out)
      </p>
    </div>
  );
}

export default DailyTokensChart;
