import { parseStepJson, stepQuestions } from '../services/workflowsService';
import ClarifyingQuestions from './ClarifyingQuestions';

function formatDate(value) {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

/** The tools a step called, if any. ToolCallsJson is "[]" for an agent-level step. */
function toolNames(step) {
  const calls = parseStepJson(step.toolCallsJson);
  return Array.isArray(calls) ? calls.map((call) => call?.tool).filter(Boolean) : [];
}

/**
 * The workflow's audit trail — every agent turn and every tool call, oldest first.
 *
 * Presentational only: it receives the steps and fetches nothing. Two kinds of row arrive
 * here and both are rendered the same way, because they are the same kind of record: the
 * API's InternalToolsController writes one per tool call (including ones it rejects), and
 * the background runner writes one per agent run.
 */
export function WorkflowSteps({ steps }) {
  return (
    <ol className="steps">
      {steps.map((step) => {
        const questions = stepQuestions(step);
        const tools = toolNames(step);
        const failed = step.validationResult && step.validationResult !== 'Ok';

        return (
          <li key={step.id} className="steps__item">
            <div className="steps__head">
              <strong className="steps__agent">{step.agentName}</strong>

              <span className={`state ${failed ? 'state--Failed' : ''}`}>
                {step.validationResult ?? 'Unknown'}
              </span>

              <span className="steps__meta">
                {step.durationMs} ms · {formatDate(step.createdAt)}
              </span>
            </div>

            {tools.length > 0 ? (
              <p className="steps__tools">Tools called: {tools.join(', ')}</p>
            ) : null}

            {step.errorMessage ? (
              <p className="steps__error">{step.errorMessage}</p>
            ) : null}

            {questions.length > 0 ? <ClarifyingQuestions questions={questions} /> : null}
          </li>
        );
      })}
    </ol>
  );
}

export default WorkflowSteps;
