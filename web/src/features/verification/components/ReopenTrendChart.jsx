import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import { formatPercent, trendChartRows } from '../services/verificationApi';
import styles from '../verification.module.css';

/** The hover text: the API's rate with the count behind it, or plainly nothing. */
function TrendTooltip({ active, payload }) {
  if (!active || !payload?.length) return null;
  const row = payload[0].payload;

  return (
    <div className={styles.tooltip}>
      <strong>{row.label}</strong>
      <span>
        {row.answered > 0 ? `${formatPercent(row.rate)} reopened — ${row.reopened} of ${row.answered} answered` : 'No answered checks this month'}
      </span>
    </div>
  );
}

/**
 * Reopen rate per month, oldest first — six points, as the API sends them.
 *
 * A month with no answered checks has no rate, and the line breaks there (see trendChartRows)
 * rather than dipping to a 0% nobody measured. The y-axis is fixed at 0–100 so a single bad
 * month is not stretched into a cliff.
 */
export function ReopenTrendChart({ monthlyTrend }) {
  const rows = trendChartRows(monthlyTrend);

  return (
    <div className={styles.chart} role="img" aria-label="Reopen rate by month, as a line chart">
      <ResponsiveContainer width="100%" height={260}>
        <AreaChart data={rows} margin={{ top: 12, right: 12, bottom: 0, left: -12 }}>
          <defs>
            <linearGradient id="reopen-fill" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#c2341d" stopOpacity={0.18} />
              <stop offset="100%" stopColor="#c2341d" stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid stroke="#eceef2" vertical={false} />
          <XAxis dataKey="label" tick={{ fill: '#6a707c', fontSize: 12 }} tickLine={false} axisLine={{ stroke: '#e3e6eb' }} />
          <YAxis
            domain={[0, 100]}
            ticks={[0, 25, 50, 75, 100]}
            tickFormatter={(value) => `${value}%`}
            tick={{ fill: '#9aa0aa', fontSize: 11, fontFamily: 'JetBrains Mono, monospace' }}
            tickLine={false}
            axisLine={false}
          />
          <Tooltip content={<TrendTooltip />} cursor={{ stroke: '#e3e6eb', strokeWidth: 1 }} />
          <Area
            type="monotone"
            dataKey="rate"
            stroke="#c2341d"
            strokeWidth={2.25}
            fill="url(#reopen-fill)"
            dot={{ r: 4, fill: '#ffffff', stroke: '#c2341d', strokeWidth: 2 }}
            activeDot={{ r: 5.5, fill: '#c2341d', stroke: '#ffffff', strokeWidth: 2 }}
            connectNulls={false}
            isAnimationActive={false}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}

export default ReopenTrendChart;
