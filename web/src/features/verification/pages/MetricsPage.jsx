import { useState } from 'react';

import { formatDateOnly } from '../../assets/services/assetsApi';
import CategoryReopenTable from '../components/CategoryReopenTable';
import ClarificationStats from '../components/ClarificationStats';
import MetricsPanel from '../components/MetricsPanel';
import ReopenTrendChart from '../components/ReopenTrendChart';
import RepeatFailureTable from '../components/RepeatFailureTable';
import useMetrics from '../hooks/useMetrics';
import { formatPercent } from '../services/verificationApi';

const EMPTY_RANGE = { fromDate: '', toDate: '' };

/**
 * The estate's three numbers, from GET /api/analytics/metrics: do repairs hold, is the
 * clarifier asking good questions, and which machines keep failing.
 *
 * EVERY FIGURE ON THIS PAGE IS THE API'S. Nothing here divides, averages, sums money or
 * compares a date with today — the page formats and draws, and that is all. FacilitiesManager
 * and Admin only, like the endpoint; a Reporter is never shown the link.
 *
 * One request feeds all three panels, and each renders its own loading, empty and error
 * state from it, so no chart area is ever blank.
 */
export function MetricsPage() {
  const [range, setRange] = useState(EMPTY_RANGE);

  // The API refuses a range that ends before it starts, so an inverted one is not sent —
  // the panels keep showing all time and the hint says why.
  const rangeError =
    range.fromDate && range.toDate && range.fromDate > range.toDate
      ? '"From" is after "To". Showing all time until the range is fixed.'
      : null;

  const { data, isLoading, error } = useMetrics(rangeError ? EMPTY_RANGE : range);

  function handleRangeChange(name, value) {
    setRange((current) => ({ ...current, [name]: value }));
  }

  const reopen = data?.reopenRate;
  const clarification = data?.clarification;
  const trendIsEmpty = !reopen || reopen.monthlyTrend.every((month) => month.answered === 0);

  return (
    <section className="page metrics-page">
      <header className="page-header">
        <div>
          <p className="page-header__eyebrow">Analytics</p>
          <h1>Metrics</h1>
          <p className="page__lead">
            Whether repairs hold, how well the clarifier asks, and which machines keep
            failing. Every figure is counted by the API; none is estimated by a model.
          </p>
        </div>
      </header>

      <div className="asset-filters report-filters">
        <div className="asset-filters__selects">
          <label className="asset-filters__select">
            <span>From</span>
            <input
              type="date"
              value={range.fromDate}
              max={range.toDate || undefined}
              onChange={(event) => handleRangeChange('fromDate', event.target.value)}
              aria-invalid={rangeError ? 'true' : undefined}
            />
          </label>
          <label className="asset-filters__select">
            <span>To</span>
            <input
              type="date"
              value={range.toDate}
              min={range.fromDate || undefined}
              onChange={(event) => handleRangeChange('toDate', event.target.value)}
              aria-invalid={rangeError ? 'true' : undefined}
            />
          </label>
          <button
            type="button"
            className="asset-filters__clear"
            onClick={() => setRange(EMPTY_RANGE)}
            disabled={!range.fromDate && !range.toDate}
          >
            All time
          </button>
        </div>
        <p className={`report-filters__hint ${rangeError ? 'report-filters__hint--error' : ''}`.trim()}>
          {rangeError ??
            'UTC days, both ends included. Checks are counted by when they fell due, reports by when they were filed.'}
        </p>
      </div>

      <MetricsPanel
        title="Reopen rate"
        lead={
          reopen && !trendIsEmpty
            ? `${formatPercent(reopen.reopenRate)} of answered checks reopened — ${reopen.reopened} of ${reopen.answered}. Only checks a reporter answered count; silence is in neither column.`
            : 'Reopened checks as a share of the checks a reporter answered, by month.'
        }
        isLoading={isLoading}
        error={error}
        isEmpty={trendIsEmpty}
        emptyBody="No reporter has answered a verification check in these six months. The trend appears once repairs start being confirmed or reopened."
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
      >
        <ClarificationStats clarification={clarification} />
      </MetricsPanel>

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
      >
        <RepeatFailureTable failures={data?.repeatFailures ?? []} />
      </MetricsPanel>
    </section>
  );
}

export default MetricsPage;
