import { formatDateTime } from '../services/verificationApi';
import AgentOutcomeBadge from './AgentOutcomeBadge';

/**
 * The VerificationAgent's verdict on the repair: its outcome, its reason and the evidence it
 * cited, all verbatim.
 *
 * ADVICE, NEVER A DECISION. The check's status is the reporter's answer, set in C#; the agent's
 * label is recorded beside it for a human to read, and nothing acts on it. The evidence is
 * shown one item per line because that is where a reader checks it against the note and the
 * history.
 *
 * "Not judged" has two different meanings, told apart by AgentQueuedAt: not handed to the
 * agent yet, or handed over and still waiting on it. Neither is shown as a verdict.
 */
export function AgentDecisionPanel({ check }) {
  return (
    <section className="approval-panel" aria-label="The agent's decision">
      <header className="approval-panel__head">
        <h3>Agent&apos;s decision</h3>
        <span className="approval-panel__source">VerificationAgent · advice</span>
      </header>

      <AgentDecisionBody check={check} />
    </section>
  );
}

function AgentDecisionBody({ check }) {
  if (!check.agentOutcome) {
    return (
      <p className="approval-panel__empty">
        {check.agentQueuedAt
          ? `Handed to the agent ${formatDateTime(check.agentQueuedAt)}; it has not given a verdict yet.`
          : 'Not handed to the agent yet — that happens once the reporter answers, or stays silent past the response window.'}
      </p>
    );
  }

  return (
    <>
      <p className="diagnosis__action">
        Verdict: <AgentOutcomeBadge outcome={check.agentOutcome} />
      </p>

      {check.agentReason ? (
        <>
          <p className="approval-panel__label">Reason</p>
          <blockquote className="approval-panel__quote">{check.agentReason}</blockquote>
        </>
      ) : null}

      <p className="approval-panel__label">Evidence</p>
      {/* Null is not empty: an outcome with no readable evidence says so rather than
          rendering an empty list that looks like "no evidence was needed". */}
      {check.agentEvidence && check.agentEvidence.length > 0 ? (
        <ul className="hypothesis__evidence verification-evidence">
          {check.agentEvidence.map((item, index) => (
            <li key={index}>{item}</li>
          ))}
        </ul>
      ) : (
        <p className="approval-panel__empty">No evidence was recorded with this verdict.</p>
      )}

      <p className="approval-panel__footnote">
        The check&apos;s status comes from the reporter&apos;s answer. This verdict is recorded
        beside it for you to weigh, and changes nothing by itself.
      </p>
    </>
  );
}

export default AgentDecisionPanel;
