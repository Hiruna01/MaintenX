import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import { formatPercent, trendChartRows } from '../services/verificationApi';

/** The hover text: the API's rate with the count behind it, or plainly nothing. */
function TrendTooltip({ active, payload }) {
  if (!active || !payload?.length) return null;
  const row = payload[0].payload;

  return (
    <div className="metrics-tooltip">
      <strong>{row.label}</strong>
      <span>
        {row.answered > 0
          ? `${formatPercent(row.rate)} reopened — ${row.reopened} of ${row.answered} answered`
          : 'No answered checks this month'}
      </span>
    </div>
  );
}

/**
 * Reopen rate per month, oldest first — six points, as the API sends them.
 *
 * A month with no answered checks has no rate, and the line breaks there (see
 * trendChartRows) rather than dipping to a 0% nobody measured. The y-axis is fixed at 0–100 so
 * a single bad month is not stretched into a cliff.
 */
export function ReopenTrendChart({ monthlyTrend }) {
  const rows = trendChartRows(monthlyTrend);

  return (
    <div className="metrics-chart" role="img" aria-label="Reopen rate by month, as a line chart">
      <ResponsiveContainer width="100%" height={260}>
        <LineChart data={rows} margin={{ top: 8, right: 16, bottom: 0, left: -8 }}>
          <CartesianGrid stroke="var(--border-soft)" vertical={false} />
          <XAxis dataKey="label" tick={{ fill: 'var(--muted)', fontSize: 12 }} tickLine={false} axisLine={{ stroke: 'var(--border)' }} />
          <YAxis
            domain={[0, 100]}
            ticks={[0, 25, 50, 75, 100]}
            tickFormatter={(value) => `${value}%`}
            tick={{ fill: 'var(--muted)', fontSize: 12 }}
            tickLine={false}
            axisLine={false}
          />
          <Tooltip content={<TrendTooltip />} />
          <Line
            type="linear"
            dataKey="rate"
            stroke="var(--danger)"
            strokeWidth={2}
            dot={{ r: 4, fill: 'var(--danger)' }}
            connectNulls={false}
            isAnimationActive={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}

export default ReopenTrendChart;
