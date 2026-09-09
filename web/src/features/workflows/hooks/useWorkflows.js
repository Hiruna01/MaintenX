import useFetch from '../../../hooks/useFetch';
import { buildWorkflowsPath } from '../services/workflowsService';

/**
 * One page of workflows from GET /api/workflows.
 *
 * Thin on purpose: it turns the page's filter state into a path via the service and hands
 * that to the shared useFetch, so the page still gets `{ data, isLoading, error }` and has
 * to render all three. `data` is the API's PagedResult:
 * `{ items, page, pageSize, totalCount, totalPages }`.
 */
export function useWorkflows({ page, pageSize, state }) {
  return useFetch(buildWorkflowsPath({ page, pageSize, state }));
}

export default useWorkflows;
