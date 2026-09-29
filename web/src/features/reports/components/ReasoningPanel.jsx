import { RefreshCw, Workflow } from 'lucide-react';
import { Link } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import { Panel } from '../../../components/ui/Panel';
import { EmptyState } from '../../../components/ui/States';
import AuditTrail from '../../workflows/components/AuditTrail';
import { describeStep, groupByWorkflow } from '../services/agentSteps';
import styles from '../reports.module.css';

/**
 * The agent reasoning for one report: every AgentStep recorded on its behalf, OLDEST FIRST,
 * across every workflow raised for it. The order is the API's and nothing re-sorts it; steps
 * are split by WorkflowId only where the id changes, so a second run is tellable apart
 * without losing the single timeline.
 *
 * Each group is the workflow page's own audit trail, so a step reads the same in both places.
 * The tally is for orientation — nothing acts on it.
 */
export function ReasoningPanel({ steps, canOpenWorkflows, onRefresh }) {
  const described = steps.map(describeStep);
  const toolCalls = described.filter((step) => step.kind === 'tool').length;
  const failures = described.filter((step) => step.outcome.failed).length;
  const groups = groupByWorkflow(steps);

  return (
    <Panel
      eyebrow="Agent reasoning"
      count={steps.length || null}
      actions={
        <MxButton size="sm" icon={RefreshCw} onClick={onRefresh}>
          Refresh
        </MxButton>
      }
    >
      <p className={styles.sectionLead}>
        Every action an agent took on this report, oldest first, as recorded. Tool calls are written by the API as
        they happen — including any it refused.
      </p>

      {steps.length === 0 ? (
        // An empty trail is not a failure: a report filed a moment ago is queued.
        <EmptyState
          compact
          icon={Workflow}
          title="No agent activity recorded yet"
          body="The report is queued and the background runner has not reached it. Refresh in a moment."
        />
      ) : (
        <>
          <ul className={styles.tally} aria-label="Step counts">
            <li>
              <strong>{steps.length}</strong> {steps.length === 1 ? 'step' : 'steps'}
            </li>
            <li>
              <strong>{toolCalls}</strong> {toolCalls === 1 ? 'tool call' : 'tool calls'}
            </li>
            <li data-failed={failures > 0}>
              <strong>{failures}</strong> failed
            </li>
          </ul>

          {groups.map((group) => (
            <div key={`${group.workflowId}-${group.steps[0].id}`} className={styles.group}>
              <p className={styles.groupHead}>
                <Workflow aria-hidden="true" />
                {canOpenWorkflows ? (
                  <Link to={`/workflows/${group.workflowId}`}>Workflow #{group.workflowId}</Link>
                ) : (
                  <span>Workflow #{group.workflowId}</span>
                )}
                <span className={styles.groupCount}>
                  {group.steps.length} {group.steps.length === 1 ? 'step' : 'steps'}
                </span>
              </p>
              <AuditTrail steps={group.steps} showTally={false} />
            </div>
          ))}
        </>
      )}
    </Panel>
  );
}

export default ReasoningPanel;
