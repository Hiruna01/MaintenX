import { ChevronRight, FileText } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';

import { formatDuration, formatInstant, timeAgo } from '../../../components/ui/format';
import { StatusPill } from '../../../components/ui/Pill';
import rows from '../../../components/ui/rows.module.css';
import { workflowStateLabel } from '../services/workflowsService';
import LifecycleMeter from './LifecycleMeter';

/** How long the run took, when it has finished — display arithmetic on two instants. */
function ranFor(workflow) {
  if (!workflow.startedAt || !workflow.completedAt) return null;
  return formatDuration(new Date(workflow.completedAt) - new Date(workflow.startedAt));
}

/** Presentational only — it receives the items it renders and fetches nothing. */
export function WorkflowRows({ workflows }) {
  const navigate = useNavigate();

  return (
    <div className={rows.wrap}>
      <table className={rows.table}>
        <caption className="mx-visually-hidden">Agent workflows, newest first</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Objective</th>
            <th scope="col">Lifecycle</th>
            <th scope="col">State</th>
            <th scope="col">Created</th>
            <th scope="col">
              <span className="mx-visually-hidden">Open</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {workflows.map((workflow, index) => {
            const took = ranFor(workflow);
            return (
              <tr
                key={workflow.id}
                className={rows.row}
                style={{ animationDelay: `${Math.min(index, 10) * 28}ms` }}
                onClick={(event) => {
                  if (event.target.closest('a')) return;
                  navigate(`/workflows/${workflow.id}`);
                }}
              >
                <td className={rows.id}>#{workflow.id}</td>
                <td className={rows.main}>
                  <Link className={rows.title} to={`/workflows/${workflow.id}`}>
                    {workflow.objective}
                  </Link>
                  <span className={rows.sub}>
                    {workflow.reportId ? (
                      <Link to={`/reports/${workflow.reportId}`} style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                        <FileText aria-hidden="true" size={13} />
                        Report #{workflow.reportId}
                      </Link>
                    ) : (
                      <span>No report</span>
                    )}
                    {workflow.startedAt ? <span>Started {timeAgo(workflow.startedAt)}</span> : <span>Queued</span>}
                    {took ? <span>Ran {took}</span> : null}
                    {workflow.outcome ? <span>Outcome: {workflow.outcome}</span> : null}
                  </span>
                </td>
                <td>
                  <LifecycleMeter state={workflow.currentState} />
                </td>
                <td>
                  <StatusPill status={workflow.currentState} label={workflowStateLabel(workflow.currentState)} />
                </td>
                <td>
                  <span className={rows.time}>{formatInstant(workflow.createdAt)}</span>
                  <span className={rows.timeSub}>{timeAgo(workflow.createdAt)}</span>
                </td>
                <td className={rows.chevron} aria-hidden="true">
                  <ChevronRight />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export default WorkflowRows;
