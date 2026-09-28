import clsx from 'clsx';
import { Link } from 'react-router-dom';

import { formatInstant } from '../../../components/ui/format';
import { Panel } from '../../../components/ui/Panel';
import { Pill } from '../../../components/ui/Pill';
import { adviceLabel } from '../services/workOrdersApi';
import styles from '../workorders.module.css';

const CONFIDENCE_TONES = { high: 'violet', medium: 'blue', low: 'slate' };

/**
 * The diagnostic agent's reading of the fault: its candidate causes with the evidence each
 * stands on, the one it rates most likely, and what it suggests happens next.
 *
 * ADVICE, NEVER A DECISION. "Replace" here is the model's opinion, recorded for the manager;
 * whether money is spent is the approval they give or withhold. The evidence is shown
 * verbatim because it is where the agent cites the dated visits it is going on.
 *
 * `title` labels each run when a reopened repair was diagnosed again; `reportId` may be null
 * for a workflow started from a bare objective.
 */
export function DiagnosisPanel({ diagnosis, reportId, title = 'Diagnosis' }) {
  return (
    <Panel eyebrow={title} actions={<span className={styles.source}>Diagnostic agent · advice</span>}>
      <DiagnosisBody diagnosis={diagnosis} reportId={reportId} />
    </Panel>
  );
}

function DiagnosisBody({ diagnosis, reportId }) {
  // Null is not empty: no step recorded means it never ran. There is no such thing as an
  // empty diagnosis, so none is invented.
  if (!diagnosis) {
    return <p className={styles.empty}>No diagnosis recorded — the diagnostic has not run on this report.</p>;
  }

  if (diagnosis.validationResult !== 'Ok') {
    return (
      <p className={clsx(styles.empty, styles.emptyFailed)}>
        The diagnostic ran ({formatInstant(diagnosis.recordedAt)}) but could not produce a diagnosis
        {diagnosis.errorMessage ? `: ${diagnosis.errorMessage}` : '.'}
      </p>
    );
  }

  if (!diagnosis.outputReadable) {
    return (
      <p className={clsx(styles.empty, styles.emptyFailed)}>
        A diagnosis was recorded but could not be read. <ReasoningLink reportId={reportId} lead="The raw output is in the" />
      </p>
    );
  }

  const primary = diagnosis.hypotheses[diagnosis.primaryHypothesisIndex];
  const others = diagnosis.hypotheses.filter((_, index) => index !== diagnosis.primaryHypothesisIndex);

  return (
    <div className={styles.diagnosis}>
      <div className={styles.primaryCause}>
        <p className={styles.miniLabel}>Most likely cause</p>
        <Hypothesis hypothesis={primary} />
      </div>

      <p className={styles.suggests}>
        Suggests
        <Pill tone={diagnosis.recommendedNextAction === 'replace' ? 'red' : 'blue'} dot={false}>
          {adviceLabel(diagnosis.recommendedNextAction)}
        </Pill>
      </p>

      {diagnosis.reasoningSummary ? <blockquote className={styles.quote}>{diagnosis.reasoningSummary}</blockquote> : null}

      {others.length > 0 ? (
        <div>
          <p className={styles.miniLabel}>Also considered</p>
          <ul className={styles.others}>
            {others.map((hypothesis) => (
              <li key={hypothesis.cause}>
                <Hypothesis hypothesis={hypothesis} />
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <p className={styles.footnote}>
        From workflow #{diagnosis.workflowId}, recorded {formatInstant(diagnosis.recordedAt)}.{' '}
        <ReasoningLink reportId={reportId} lead="Every step the agents took is in the" />
      </p>
    </div>
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
    <div className={styles.hypothesis}>
      <p className={styles.cause}>
        <span>{hypothesis.cause}</span>
        <Pill tone={CONFIDENCE_TONES[hypothesis.confidence] ?? 'slate'} dot={false}>
          {adviceLabel(hypothesis.confidence)} confidence
        </Pill>
      </p>
      <ul className={styles.evidence} aria-label="Evidence">
        {hypothesis.evidence.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  );
}

export default DiagnosisPanel;
