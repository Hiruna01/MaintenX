import { useState } from 'react';

import { formatDateTime } from '../services/reportsApi';
import { describeStep, prettyJson } from '../services/agentSteps';

/**
 * One recorded agent action, as a readable row: who acted, what kind of action it was, how
 * long it took, a plain outcome, and one sentence saying what happened. A failed step shows
 * its reason in the row itself, not behind the toggle.
 *
 * The raw record — PayloadJson, ToolCallsJson and the ValidationResult tag exactly as
 * stored — is behind "Show raw". It is the audit trail and it is never edited, so it is
 * always available; it is just not the first thing a reader should have to parse.
 */
export function AgentStepRow({ step, number }) {
  const [showRaw, setShowRaw] = useState(false);
  const { kind, tool, outcome, summary, questions } = describeStep(step);
  const rawId = `agent-step-raw-${step.id}`;

  return (
    <li className={`agent-step ${outcome.failed ? 'agent-step--failed' : ''}`.trim()}>
      <span className="agent-step__number" aria-hidden="true">
        {number}
      </span>

      <div className="agent-step__body">
        <header className="agent-step__head">
          <strong className="agent-step__agent">{step.agentName}</strong>
          <span className="agent-step__kind">{kind === 'tool' ? 'Tool call' : 'Agent run'}</span>
          {tool ? <code className="agent-step__tool">{tool.tool}</code> : null}
          <span className={`step-outcome step-outcome--${outcome.tone}`}>{outcome.label}</span>
          <span className="agent-step__duration">{step.durationMs.toLocaleString()} ms</span>
        </header>

        <p className="agent-step__summary">{summary}</p>

        {outcome.failed || step.errorMessage ? (
          <div className="agent-step__reason" role="note">
            <span className="agent-step__reason-label">Reason</span>
            <p>{step.errorMessage ?? 'No reason was recorded for this failure.'}</p>
          </div>
        ) : null}

        {/* The questions as the agent produced them, verbatim — the audit copy. The
            ClarificationQuestion rows above are the working copy the reporter answers. */}
        {questions && questions.length > 0 ? (
          <ol className="agent-step__questions">
            {questions.map((question, index) => (
              // No stable id in an agent payload, and the list is rendered once as stored.
              // eslint-disable-next-line react/no-array-index-key
              <li key={index}>
                {question.question_text}{' '}
                <code className="agent-step__answer-type">{question.answer_type}</code>
                {Array.isArray(question.options) && question.options.length > 0 ? (
                  <span className="agent-step__options"> — {question.options.join(' / ')}</span>
                ) : null}
              </li>
            ))}
          </ol>
        ) : null}

        <footer className="agent-step__foot">
          <time dateTime={step.createdAt}>{formatDateTime(step.createdAt)}</time>
          <button
            type="button"
            className="agent-step__raw-toggle"
            aria-expanded={showRaw}
            aria-controls={rawId}
            onClick={() => setShowRaw((current) => !current)}
          >
            {showRaw ? 'Hide raw' : 'Show raw'}
          </button>
        </footer>

        {showRaw ? (
          <dl className="agent-step__raw" id={rawId}>
            <dt>ValidationResult</dt>
            <dd>
              <pre>{step.validationResult ?? 'null'}</pre>
            </dd>
            <dt>ToolCallsJson</dt>
            <dd>
              <pre>{step.toolCallsJson ? prettyJson(step.toolCallsJson) : 'null'}</pre>
            </dd>
            <dt>PayloadJson</dt>
            <dd>
              <pre>{step.payloadJson ? prettyJson(step.payloadJson) : 'null'}</pre>
            </dd>
          </dl>
        ) : null}
      </div>
    </li>
  );
}

export default AgentStepRow;
