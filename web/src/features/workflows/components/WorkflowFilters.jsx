import { WORKFLOW_STATES, workflowStateLabel } from '../services/workflowsService';

/**
 * Search box and state filter. Both are controlled by the page; this component only
 * renders them and reports changes upward.
 */
export function WorkflowFilters({ search, onSearchChange, state, onStateChange }) {
  return (
    <div className="filters">
      <div className="filters__field">
        <label htmlFor="workflow-search">Search</label>
        <input
          id="workflow-search"
          type="search"
          placeholder="Objective contains…"
          value={search}
          onChange={(event) => onSearchChange(event.target.value)}
        />
        <p className="filters__hint">
          Filters the workflows on this page. The API has no search parameter yet.
        </p>
      </div>

      <div className="filters__field">
        <label htmlFor="workflow-state">State</label>
        <select
          id="workflow-state"
          value={state}
          onChange={(event) => onStateChange(event.target.value)}
        >
          <option value="">All states</option>
          {WORKFLOW_STATES.map((value) => (
            <option key={value} value={value}>
              {workflowStateLabel(value)}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}

export default WorkflowFilters;
