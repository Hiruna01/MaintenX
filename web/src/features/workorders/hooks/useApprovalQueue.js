import useFetch from '../../../hooks/useFetch';
import { buildApprovalQueuePath } from '../services/workOrdersApi';

/**
 * One page of GET /api/workorders/approvals — every order waiting on a manager, oldest
 * first, each carrying everything needed to decide it. The API's PagedResult, as is.
 */
export function useApprovalQueue(page) {
  return useFetch(buildApprovalQueuePath(page));
}

export default useApprovalQueue;
