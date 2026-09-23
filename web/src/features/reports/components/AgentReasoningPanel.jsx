import { Link } from 'react-router-dom';

import Button from '../../../components/Button';
import { describeStep, groupByWorkflow } from '../services/agentSteps';
import AgentStepRow from './AgentStepRow';

/**
 * The agent reasoning for one report: every AgentStep recorded on its behalf, OLDEST FIRST,
 * across every workflow raised for it — the workflow monitoring and execution summary.
 *
 * Presentational only; it receives the steps and fetches nothing. The order is the API's and
 * nothing re-sorts it. Steps are split by WorkflowId only where the id changes, so a second
 * run is tellable apart without losing the single timeline.
 *
 * The counts in the header are tallies of the rows below, for orientation. They are not a
 * business rule and nothing acts on them.
 */
export function AgentReasoningPanel({ steps, canOpenWorkflows, onRefresh }) {
  const described = steps.map(describeStep);
  const toolCalls = described.filter((step) => step.kind === 'tool').length;
  const failures = described.filter((step) => step.outcome.failed).length;
  const groups = groupByWorkflow(steps);
  // Numbered across the whole trail, not per workflow, so "step 4" means one thing.
  const numberOf = new Map(steps.map((step, index) => [step.id, index + 1]));

  return (
    <section className="report-section reasoning" aria-labelledby="reasoning-heading">
      <header className="report-section__head">
        <div>
          <h2 id="reasoning-heading">Agent reasoning</h2>
          <p className="report-section__lead">
            Every action an agent took on this report, oldest first, as recorded. Tool calls are
            written by the API as they happen — including any it refused.
          </p>
        </div>
        <Button variant="secondary" onClick={onRefresh}>
          Refresh
        </Button>
      </header>

      {steps.length === 0 ? (
        // An empty trail is not a failure and must not look like one: a report filed a
        // moment ago is queued, and the background runner has not reached it yet.
        <div className="empty-state">
          <p className="empty-state__title">No agent activity recorded yet</p>
          <p className="empty-state__body">
            The report is queued and the background runner has not reached it. Refresh in a
            moment.
          </p>
        </div>
      ) : (
        <>
          <ul className="reasoning__stats" aria-label="Step counts">
            <li>
              <strong>{steps.length}</strong> {steps.length === 1 ? 'step' : 'steps'}
            </li>
            <li>
              <strong>{toolCalls}</strong> {toolCalls === 1 ? 'tool call' : 'tool calls'}
            </li>
            <li className={failures > 0 ? 'reasoning__stat--failed' : undefined}>
              <strong>{failures}</strong> failed
            </li>
          </ul>

          {groups.map((group) => (
            <div key={`${group.workflowId}-${group.steps[0].id}`} className="reasoning__group">
              <p className="reasoning__workflow">
                {canOpenWorkflows ? (
                  <Link to={`/workflows/${group.workflowId}`}>Workflow #{group.workflowId}</Link>
                ) : (
                  <span>Workflow #{group.workflowId}</span>
                )}
              </p>

              <ol className="reasoning__steps">
                {group.steps.map((step) => (
                  <AgentStepRow key={step.id} step={step} number={numberOf.get(step.id)} />
                ))}
              </ol>
            </div>
          ))}
        </>
      )}
    </section>
  );
}

export default AgentReasoningPanel;
