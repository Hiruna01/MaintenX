import useFetch from '../../../hooks/useFetch';
import { buildWorkflowPath } from '../services/workflowsService';

/**
 * One workflow with its steps, from GET /api/workflows/{id}.
 *
 * Thin, like useWorkflows: it turns an id into a path via the service and hands that to
 * the shared useFetch, so the page still gets `{ data, isLoading, error }` and has to
 * render all three. `data` is the API's WorkflowDetailDto.
 */
export function useWorkflow(id) {
  return useFetch(buildWorkflowPath(id));
}

export default useWorkflow;
