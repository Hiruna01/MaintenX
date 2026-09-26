import { Link } from 'react-router-dom';

import { adviceLabel, formatDateTime } from '../services/workOrdersApi';

/**
 * The diagnostic agent's reading of the fault: its candidate causes with the evidence each
 * stands on, the one it rates most likely, and what it suggests happens next.
 *
 * ADVICE, NEVER A DECISION. "Replace" here is the model's opinion, recorded for the manager;
 * whether money is spent is the approval they are about to give or withhold. The evidence is
 * shown verbatim because it is where the agent cites the dated visits it is going on — and
 * the service history beside this panel is there to check it against.
 *
 * `title` lets the workflow page label each run when a reopened repair was diagnosed again;
 * `reportId` may be null there, for a workflow started from a bare objective.
 */
export function DiagnosisPanel({ diagnosis, reportId, title = 'Diagnosis' }) {
  return (
    <section className="approval-panel" aria-label={title}>
      <header className="approval-panel__head">
        <h3>{title}</h3>
        <span className="approval-panel__source">DiagnosticAgent · advice</span>
      </header>

      <DiagnosisBody diagnosis={diagnosis} reportId={reportId} />
    </section>
  );
}

function DiagnosisBody({ diagnosis, reportId }) {
  // Null is not empty: no step recorded means it never ran. There is no such thing as an
  // empty diagnosis, so none is invented.
  if (!diagnosis) {
    return (
      <p className="approval-panel__empty">
        No diagnosis recorded — the diagnostic has not run on this report.
      </p>
    );
  }

  if (diagnosis.validationResult !== 'Ok') {
    return (
      <p className="approval-panel__empty approval-panel__empty--failed">
        The diagnostic ran ({formatDateTime(diagnosis.recordedAt)}) but could not produce a
        diagnosis{diagnosis.errorMessage ? `: ${diagnosis.errorMessage}` : '.'}
      </p>
    );
  }

  if (!diagnosis.outputReadable) {
    return (
      <p className="approval-panel__empty approval-panel__empty--failed">
        A diagnosis was recorded but could not be read.{' '}
        <ReasoningLink reportId={reportId} lead="The raw output is in the" />
      </p>
    );
  }

  const primary = diagnosis.hypotheses[diagnosis.primaryHypothesisIndex];
  const others = diagnosis.hypotheses.filter((_, index) => index !== diagnosis.primaryHypothesisIndex);

  return (
    <>
      <div className="diagnosis__primary">
        <p className="approval-panel__label">Most likely cause</p>
        <Hypothesis hypothesis={primary} />
      </div>

      <p className="diagnosis__action">
        Suggests: <span className={`next-action next-action--${diagnosis.recommendedNextAction}`}>
          {adviceLabel(diagnosis.recommendedNextAction)}
        </span>
      </p>

      <blockquote className="approval-panel__quote">{diagnosis.reasoningSummary}</blockquote>

      {others.length > 0 ? (
        <>
          <p className="approval-panel__label">Also considered</p>
          <ul className="diagnosis__others">
            {others.map((hypothesis) => (
              <li key={hypothesis.cause}>
                <Hypothesis hypothesis={hypothesis} />
              </li>
            ))}
          </ul>
        </>
      ) : null}

      <p className="approval-panel__footnote">
        From workflow #{diagnosis.workflowId}, recorded {formatDateTime(diagnosis.recordedAt)}.{' '}
        <ReasoningLink reportId={reportId} lead="Every step the agents took is in the" />
      </p>
    </>
  );
}

/** No report, no reasoning panel to point at — a link to /reports/null would be a 404. */
function ReasoningLink({ reportId, lead }) {
  if (!reportId) return null;

  return (
    <>
      {lead} <Link to={`/reports/${reportId}`}>report&apos;s agent reasoning</Link>.
    </>
  );
}

function Hypothesis({ hypothesis }) {
  return (
    <div className="hypothesis">
      <p className="hypothesis__cause">
        {hypothesis.cause}
        <span className={`confidence confidence--${hypothesis.confidence}`}>
          {adviceLabel(hypothesis.confidence)} confidence
        </span>
      </p>
      <ul className="hypothesis__evidence" aria-label="Evidence">
        {hypothesis.evidence.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

export default DiagnosisPanel;
