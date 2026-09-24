import useFetch from '../../../hooks/useFetch';
import { buildWorkOrdersPath } from '../services/workOrdersApi';

/**
 * One page of the dispatch board from GET /api/workorders. Thin on purpose: the service
 * turns the page's filter state into a path and the shared useFetch does the rest, so the
 * page still gets `{ data, isLoading, error }` and renders all three.
 */
export function useWorkOrders(query) {
  return useFetch(buildWorkOrdersPath(query));
}

export default useWorkOrders;
