import useFetch from '../../../hooks/useFetch';
import { buildWorkOrderPath } from '../services/workOrdersApi';

/** One work order from GET /api/workorders/{id}, with its approval basis and booked visits. */
export function useWorkOrder(id) {
  return useFetch(buildWorkOrderPath(id));
}

export default useWorkOrder;
