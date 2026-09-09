import { workflowStateLabel } from '../services/workflowsService';

function formatDate(value) {
  if (!value) return '—';
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? '—' : parsed.toLocaleString();
}

/** Presentational only — it receives the items it renders and fetches nothing. */
export function WorkflowTable({ workflows }) {
  return (
    <div className="table-scroll">
      <table className="table">
        <caption className="visually-hidden">Agent workflows, newest first</caption>
        <thead>
          <tr>
            <th scope="col">#</th>
            <th scope="col">Objective</th>
            <th scope="col">State</th>
            <th scope="col">Outcome</th>
            <th scope="col">Created</th>
          </tr>
        </thead>
        <tbody>
          {workflows.map((workflow) => (
            <tr key={workflow.id}>
              <td>{workflow.id}</td>
              <td>{workflow.objective}</td>
              <td>
                <span className={`state state--${workflow.currentState}`}>
                  {workflowStateLabel(workflow.currentState)}
                </span>
              </td>
              <td>{workflow.outcome ?? '—'}</td>
              <td>{formatDate(workflow.createdAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default WorkflowTable;
