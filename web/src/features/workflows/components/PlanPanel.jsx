import { ListOrdered } from 'lucide-react';

import { Panel } from '../../../components/ui/Panel';
import { Pill } from '../../../components/ui/Pill';
import { EmptyState } from '../../../components/ui/States';
import { PLAN_STEP_STATUSES, planSourceLabel, planStepStatusLabel } from '../services/workflowsService';
import styles from '../workflows.module.css';

const STATUS_TONES = {
  [PLAN_STEP_STATUSES.completed]: 'green',
  [PLAN_STEP_STATUSES.failed]: 'red',
  [PLAN_STEP_STATUSES.skipped]: 'amber',
  [PLAN_STEP_STATUSES.pending]: 'slate',
};

/**
 * The workflow's structured plan: which agents the run was delegated to, in order, what each
 * was to establish, and how far each has got.
 *
 * Everything here is read off `WorkflowDetailDto.plan` and only displayed. Whether the plan
 * was the planner's or the API's fallback, and each step's status, were decided in C#
 * (PlanRules) — the page says which, and never works a status out for itself. A step still
 * pending on a workflow that has failed is shown as pending, because that is what it is.
 */
export function PlanPanel({ plan }) {
  if (!plan) {
    return (
      <Panel eyebrow="Plan">
        <EmptyState
          compact
          icon={ListOrdered}
          title="No plan recorded"
          body="This run started before plans were recorded, or the agent service could not be reached to plan it."
        />
      </Panel>
    );
  }

  const fromPlanner = plan.source === 'planner';

  return (
    <Panel
      eyebrow="Plan"
      count={plan.steps.length}
      actions={<Pill tone={fromPlanner ? 'violet' : 'amber'}>{planSourceLabel(plan.source)}</Pill>}
    >
      {fromPlanner && plan.rationale ? <p className={styles.planNote}>{plan.rationale}</p> : null}
      {!fromPlanner && plan.note ? <p className={styles.planNote}>{plan.note}</p> : null}

      <ol className={styles.planList}>
        {plan.steps.map((step) => (
          <li key={step.order} className={styles.planStep}>
            <span className={styles.planOrder} aria-hidden="true">
              {step.order}
            </span>
            <div className={styles.planBody}>
              <div className={styles.planHead}>
                <span className={styles.planAgent}>{step.agent}</span>
                <Pill tone={STATUS_TONES[step.status] ?? 'slate'}>{planStepStatusLabel(step.status)}</Pill>
                {fromPlanner && step.addedBy === 'api' ? (
                  <span className={styles.planAddedBy}>added by the API</span>
                ) : null}
              </div>
              <p className={styles.planPurpose}>{step.purpose}</p>
            </div>
          </li>
        ))}
      </ol>
    </Panel>
  );
}

export default PlanPanel;
