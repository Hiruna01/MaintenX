import { Link, useParams } from 'react-router-dom';

import ErrorMessage from '../../../components/ErrorMessage';
import Spinner from '../../../components/Spinner';
import WorkflowSteps from '../components/WorkflowSteps';
import useWorkflow from '../hooks/useWorkflow';
import { workflowStateLabel } from '../services/workflowsService';

function formatDate(value) {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

/**
 * One workflow and its audit trail.
 *
 * NOTE what is not on this page: the workflow's PlanJson. Nothing populates it — the
 * clarifier produces questions, and questions are not a plan — so rendering it would put
 * a permanently empty "Plan" heading on the page. It comes back when an agent actually
 * produces a plan.
 */
export function WorkflowDetailPage() {
  const { id } = useParams();
  const { data, isLoading, error } = useWorkflow(id);

  return (
    <section className="page">
      <p className="page__back">
        <Link to="/workflows">← All workflows</Link>
      </p>

      <h1>Workflow {id}</h1>

      {/* All three request states are rendered explicitly. A blank screen is a bug. */}
      {isLoading ? <Spinner label="Loading workflow…" /> : null}

      {!isLoading && error ? (
        <ErrorMessage title="Could not load this workflow" message={error.message} />
      ) : null}

      {!isLoading && !error && data ? (
        <>
          <p className="page__lead">{data.objective}</p>

          <dl className="detail">
            <div className="detail__row">
              <dt>State</dt>
              <dd>
                <span className={`state state--${data.currentState}`}>
                  {workflowStateLabel(data.currentState)}
                </span>
              </dd>
            </div>
            <div className="detail__row">
              <dt>Outcome</dt>
              <dd>{data.outcome ?? '—'}</dd>
            </div>
            <div className="detail__row">
              <dt>Report</dt>
              <dd>
                {data.reportId ? (
                  <Link to={`/reports/${data.reportId}`}>Report #{data.reportId}</Link>
                ) : (
                  'Started without a report'
                )}
              </dd>
            </div>
            <div className="detail__row">
              <dt>Started</dt>
              <dd>{formatDate(data.startedAt)}</dd>
            </div>
            <div className="detail__row">
              <dt>Completed</dt>
              <dd>{formatDate(data.completedAt)}</dd>
            </div>
          </dl>

          <h2>Steps</h2>

          {/* An empty trail is not a failure and must not look like one: a workflow that
              has only just been queued genuinely has no steps yet. */}
          {data.steps.length === 0 ? (
            <p className="empty">
              No steps recorded yet. This workflow is queued and the background runner has
              not reached it.
            </p>
          ) : (
            <WorkflowSteps steps={data.steps} />
          )}
        </>
      ) : null}
    </section>
  );
}

export default WorkflowDetailPage;
