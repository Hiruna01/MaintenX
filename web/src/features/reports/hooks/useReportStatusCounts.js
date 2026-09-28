import useFetch from '../../../hooks/useFetch';
import { buildReportsPath } from '../services/reportsApi';

/**
 * How many reports each status tab would show under the other filters applied now. One
 * page-of-one request per tab; each count is the API's `totalCount` for this caller. A count
 * that has not arrived (or failed) is null and renders as "·".
 */
export function useReportStatusCounts({ search, dateFrom, dateTo }) {
  const base = { search, dateFrom, dateTo, page: 1, pageSize: 1 };
  const all = useFetch(buildReportsPath({ ...base, status: '' }));
  const submitted = useFetch(buildReportsPath({ ...base, status: 'Submitted' }));
  const awaiting = useFetch(buildReportsPath({ ...base, status: 'AwaitingClarification' }));
  const clarified = useFetch(buildReportsPath({ ...base, status: 'Clarified' }));
  const diagnosed = useFetch(buildReportsPath({ ...base, status: 'Diagnosed' }));
  const raised = useFetch(buildReportsPath({ ...base, status: 'WorkOrderRaised' }));
  const closed = useFetch(buildReportsPath({ ...base, status: 'Closed' }));

  const count = (result) => (result.isLoading || result.error ? null : result.data?.totalCount ?? null);

  return {
    '': count(all),
    Submitted: count(submitted),
    AwaitingClarification: count(awaiting),
    Clarified: count(clarified),
    Diagnosed: count(diagnosed),
    WorkOrderRaised: count(raised),
    Closed: count(closed),
  };
}

export default useReportStatusCounts;
