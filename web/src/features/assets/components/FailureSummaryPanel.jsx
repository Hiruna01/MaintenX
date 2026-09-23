import { enumLabel, formatDateOnly } from '../services/assetsApi';
import OutcomeBadge from './OutcomeBadge';

function Stat({ label, value, detail, tone }) {
  return (
    <div className={`summary-stat ${tone ? `summary-stat--${tone}` : ''}`.trim()}>
      <dt className="summary-stat__label">{label}</dt>
      <dd className="summary-stat__value">{value}</dd>
      {detail ? <dd className="summary-stat__detail">{detail}</dd> : null}
    </div>
  );
}

/**
 * The asset's failure summary, exactly as the API computed it.
 *
 * Presentational only. Nothing here is derived: the counts, the 90-day window, the
 * repeat-failure flag and the days since the last visit are all computed in C# from the
 * materialised history, and the diagnostic agent reads this same summary rather than
 * producing it. The panel's footnote says so, because a reader deciding whether to replace
 * a machine should know these figures are arithmetic, not an opinion.
 */
export function FailureSummaryPanel({ summary }) {
  // Null is not zero: a machine nobody has ever touched is not a machine serviced today.
  const neverServiced = summary.lastServicedOn === null;

  return (
    <section className="summary-panel" aria-labelledby="failure-summary-heading">
      <header className="summary-panel__head">
        <h2 id="failure-summary-heading">Failure summary</h2>
        {summary.isRepeatFailure ? (
          <span className="repeat-flag">Repeat failure</span>
        ) : null}
      </header>

      {summary.isRepeatFailure ? (
        <p className="summary-panel__alert">
          Three or more service visits in the last 90 days. Read the history below in order —
          the pattern is in the notes, not on the asset record.
        </p>
      ) : null}

      <dl className="summary-panel__grid">
        <Stat
          label="Visits · last 90 days"
          value={summary.failureCount3Months}
          tone={summary.isRepeatFailure ? 'alert' : undefined}
        />
        <Stat label="Visits · last 12 months" value={summary.failureCount12Months} />
        <Stat
          label="Temporary fixes"
          value={summary.temporaryFixCount}
          detail="Across the whole history"
          tone={summary.temporaryFixCount > 0 ? 'warn' : undefined}
        />
        <Stat
          label="Last serviced"
          value={neverServiced ? 'Never' : formatDateOnly(summary.lastServicedOn)}
          detail={
            neverServiced
              ? 'No service visit on record'
              : `${summary.daysSinceLastService} ${summary.daysSinceLastService === 1 ? 'day' : 'days'} ago`
          }
        />
      </dl>

      <div className="summary-panel__outcomes">
        <span className="summary-panel__outcomes-label">Outcomes on record</span>
        {summary.distinctOutcomes.length === 0 ? (
          <span className="summary-panel__none">None yet</span>
        ) : (
          <ul aria-label="Outcomes on record">
            {summary.distinctOutcomes.map((outcome) => (
              <li key={outcome} title={enumLabel(outcome)}>
                <OutcomeBadge outcome={outcome} />
              </li>
            ))}
          </ul>
        )}
      </div>

      <p className="summary-panel__footnote">
        Counts and date comparisons computed by the API from the service history — not an
        agent&apos;s assessment. &ldquo;90 days&rdquo; means exactly 90.
      </p>
    </section>
  );
}

export default FailureSummaryPanel;
