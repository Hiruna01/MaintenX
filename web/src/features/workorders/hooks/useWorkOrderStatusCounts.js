import useFetch from '../../../hooks/useFetch';
import { buildWorkOrdersPath } from '../services/workOrdersApi';

/**
 * How many orders each status tab would show for this caller, under the search and
 * technician filter applied now. One page-of-one request per tab; each count is the API's
 * `totalCount` — a Technician's counts are their own queue's, because the API scopes it.
 */
export function useWorkOrderStatusCounts({ search, technicianId }) {
  const base = { search, technicianId, page: 1, pageSize: 1 };
  const all = useFetch(buildWorkOrdersPath({ ...base, status: '' }));
  const draft = useFetch(buildWorkOrdersPath({ ...base, status: 'Draft' }));
  const awaiting = useFetch(buildWorkOrdersPath({ ...base, status: 'AwaitingApproval' }));
  const approved = useFetch(buildWorkOrdersPath({ ...base, status: 'Approved' }));
  const rejected = useFetch(buildWorkOrdersPath({ ...base, status: 'Rejected' }));
  const scheduled = useFetch(buildWorkOrdersPath({ ...base, status: 'Scheduled' }));
  const inProgress = useFetch(buildWorkOrdersPath({ ...base, status: 'InProgress' }));
  const completed = useFetch(buildWorkOrdersPath({ ...base, status: 'Completed' }));
  const cancelled = useFetch(buildWorkOrdersPath({ ...base, status: 'Cancelled' }));

  const count = (result) => (result.isLoading || result.error ? null : result.data?.totalCount ?? null);

  return {
    '': count(all),
    Draft: count(draft),
    AwaitingApproval: count(awaiting),
    Approved: count(approved),
    Rejected: count(rejected),
    Scheduled: count(scheduled),
    InProgress: count(inProgress),
    Completed: count(completed),
    Cancelled: count(cancelled),
  };
}

export default useWorkOrderStatusCounts;
