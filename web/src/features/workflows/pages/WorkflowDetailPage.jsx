import { CalendarClock, CircleCheck, FileText, ListTree, RotateCcw, Timer } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';

import MxButton from '../../../components/ui/Button';
import { formatDuration, formatInstant, timeAgo } from '../../../components/ui/format';
import PageHeader from '../../../components/ui/PageHeader';
import { Panel } from '../../../components/ui/Panel';
import { StatusPill } from '../../../components/ui/Pill';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import AuditTrail from '../components/AuditTrail';
import DiagnosisComparison from '../components/DiagnosisComparison';
import LifecycleRail from '../components/LifecycleRail';
import PlanPanel from '../components/PlanPanel';
import RunAgainButton from '../components/RunAgainButton';
import useWorkflow from '../hooks/useWorkflow';
import { workflowStateLabel } from '../services/workflowsService';
import styles from '../workflows.module.css';

function DetailSkeleton() {
  return (
    <div role="status" aria-label="Loading workflow">
      <Skeleton width={160} height={12} style={{ marginBottom: 18 }} />
      <Skeleton width="30%" height={40} style={{ marginBottom: 28 }} />
      <div className={styles.detailGrid}>
        <div className={styles.column}>
          <Skeleton height={120} radius={20} />
          <Skeleton height={130} radius={20} />
          <Skeleton height={360} radius={20} />
        </div>
        <Skeleton height={300} radius={20} />
      </div>
    </div>
  );
}

function Fact({ icon: Icon, label, children }) {
  return (
    <div className={styles.fact}>
      <dt>
        <Icon aria-hidden="true" strokeWidth={1.7} />
        {label}
      </dt>
      <dd>{children}</dd>
    </div>
  );
}

/**
 * One workflow: its objective, the plan it was delegated from, where it sits in the
 * lifecycle, its diagnoses and its audit trail.
 *
 * The plan is `data.plan` — PlanJson as the API stored it, after its PlanRules check: the
 * planner agent's plan, or the default one with the reason. The page shows it and says which;
 * it decides nothing about it. The raw planner reply is on the planner's step in the trail.
 */
export function WorkflowDetailPage() {
  const { id } = useParams();
  const { data, isLoading, error } = useWorkflow(id);

  if (isLoading) {
    return (
      <section className={styles.page}>
        <DetailSkeleton />
      </section>
    );
  }

  if (error || !data) {
    return (
      <section className={styles.page}>
        <PageHeader crumbs={[{ label: 'Workflows', to: '/workflows' }, { label: `#${id}` }]} title={`Workflow #${id}`} />
        <Panel>
          <ErrorState
            title={error?.status === 404 ? 'Workflow not found' : 'Could not load this workflow'}
            message={error?.status === 404 ? `There is no workflow with id ${id}.` : error?.message}
            action={<MxButton to="/workflows">Back to all workflows</MxButton>}
          />
        </Panel>
      </section>
    );
  }

  const ran = data.startedAt && data.completedAt ? formatDuration(new Date(data.completedAt) - new Date(data.startedAt)) : null;

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Workflows', to: '/workflows' }, { label: `#${data.id}` }]}
        title={`Workflow #${data.id}`}
        actions={
          data.reportId ? (
            <>
              <RunAgainButton workflow={data} />
              <MxButton variant="primary" icon={FileText} to={`/reports/${data.reportId}`}>
                Open report
              </MxButton>
            </>
          ) : null
        }
      >
        <div className={styles.headerPills}>
          <StatusPill status={data.currentState} label={workflowStateLabel(data.currentState)} />
          <span className={styles.headerSub}>Created {timeAgo(data.createdAt)}</span>
        </div>
      </PageHeader>

      <div className={styles.detailGrid}>
        <div className={styles.column}>
          <Panel eyebrow="Objective">
            {/* The report's description, verbatim — it is what the run was asked to act on. */}
            <blockquote className={styles.objective}>{data.objective}</blockquote>
          </Panel>

          <PlanPanel plan={data.plan} />

          <LifecycleRail state={data.currentState} reopenedWorkOrderId={data.reopenedWorkOrderId} />

          {/* Only when the diagnostic has run: a workflow still waiting on its reporter has
              no diagnosis, and an empty panel would read as one that failed. */}
          {data.diagnoses?.length > 0 ? (
            <DiagnosisComparison
              diagnoses={data.diagnoses}
              reportId={data.reportId}
              reopenedWorkOrderId={data.reopenedWorkOrderId}
            />
          ) : null}

          <Panel eyebrow="Audit trail" count={data.steps.length || null} actions={<span className={styles.panelHint}>Oldest first</span>}>
            {/* An empty trail is not a failure: a workflow only just queued has no steps yet. */}
            {data.steps.length === 0 ? (
              <EmptyState
                icon={ListTree}
                title="No steps recorded yet"
                body="This workflow is queued and the background runner has not reached it."
              />
            ) : (
              <AuditTrail steps={data.steps} />
            )}
          </Panel>
        </div>

        <aside className={styles.aside}>
          <Panel eyebrow="Details">
            <dl className={styles.facts}>
              <Fact icon={CircleCheck} label="State">
                <StatusPill status={data.currentState} label={workflowStateLabel(data.currentState)} />
              </Fact>
              <Fact icon={ListTree} label="Outcome">
                {data.outcome ?? <span className={styles.muted}>Not recorded</span>}
              </Fact>
              <Fact icon={FileText} label="Report">
                {data.reportId ? (
                  <Link to={`/reports/${data.reportId}`}>Report #{data.reportId}</Link>
                ) : (
                  <span className={styles.muted}>Started without a report</span>
                )}
              </Fact>
              <Fact icon={CalendarClock} label="Started">
                <span className="mx-mono">{formatInstant(data.startedAt)}</span>
              </Fact>
              <Fact icon={CalendarClock} label="Completed">
                <span className="mx-mono">{formatInstant(data.completedAt)}</span>
              </Fact>
              {ran ? (
                <Fact icon={Timer} label="Ran for">
                  <span className="mx-mono">{ran}</span>
                </Fact>
              ) : null}
              {data.reopenedWorkOrderId ? (
                <Fact icon={RotateCcw} label="Reopened">
                  The repair on <Link to={`/workorders/${data.reopenedWorkOrderId}`}>work order #{data.reopenedWorkOrderId}</Link>{' '}
                  did not hold
                </Fact>
              ) : null}
            </dl>
          </Panel>
        </aside>
      </div>
    </section>
  );
}

export default WorkflowDetailPage;
