import { Link } from 'react-router-dom';

import DiagnosisPanel from '../../workorders/components/DiagnosisPanel';

/**
 * Every diagnosis made on this workflow, OLDEST FIRST and side by side.
 *
 * A workflow is diagnosed once, unless a repair it led to did not hold: verification then
 * reopens it and the diagnostic runs again against the history as it stands — with the
 * repair's own service record on it — and the reports filed since. Each run is its own
 * AgentStep, so the first opinion is never overwritten, and this is where a reader sets the
 * two against each other.
 *
 * Presentational only. The API reads each step into the approval queue's diagnosis shape,
 * and the approval queue's own panel renders it, so a diagnosis reads the same everywhere.
 * Nothing here compares the runs or says which is right: both are advice.
 */
export function DiagnosisComparison({ diagnoses, reportId, reopenedWorkOrderId }) {
  const compared = diagnoses.length > 1;

  return (
    <section className="diagnosis-comparison" aria-labelledby="diagnoses-heading">
      <h2 id="diagnoses-heading">{compared ? 'Diagnoses compared' : 'Diagnosis'}</h2>

      {compared ? (
        <p className="diagnosis-comparison__lead">
          The diagnostic ran {diagnoses.length} times on this workflow. Every run after the first
          followed a repair that did not hold
          {reopenedWorkOrderId ? (
            <>
              {' '}— most recently{' '}
              <Link to={`/workorders/${reopenedWorkOrderId}`}>work order #{reopenedWorkOrderId}</Link>
            </>
          ) : null}
          , and read the service history and reports as they stood then. Oldest on the left.
        </p>
      ) : null}

      <div className={compared ? 'diagnosis-comparison__grid' : undefined}>
        {diagnoses.map((diagnosis, index) => (
          <DiagnosisPanel
            key={diagnosis.stepId}
            diagnosis={diagnosis}
            reportId={reportId}
            title={runTitle(index, compared)}
          />
        ))}
      </div>
    </section>
  );
}

function runTitle(index, compared) {
  if (!compared) return 'Diagnosis';
  return index === 0 ? 'Run 1 · first diagnosis' : `Run ${index + 1} · after a repair did not hold`;
}

export default DiagnosisComparison;
