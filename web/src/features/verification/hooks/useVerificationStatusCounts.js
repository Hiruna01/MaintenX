import useFetch from '../../../hooks/useFetch';
import { buildVerificationsPath } from '../services/verificationApi';

/**
 * How many checks each status tab would show for this caller, under the search and due-date
 * range applied now. One page-of-one request per tab; each count is the API's `totalCount`,
 * scoped by the API — a Reporter's counts are their own checks'.
 */
export function useVerificationStatusCounts({ search, dateFrom, dateTo }) {
  const base = { search, dateFrom, dateTo, page: 1, pageSize: 1 };
  const all = useFetch(buildVerificationsPath({ ...base, status: '' }));
  const pending = useFetch(buildVerificationsPath({ ...base, status: 'Pending' }));
  const awaiting = useFetch(buildVerificationsPath({ ...base, status: 'AwaitingReporterResponse' }));
  const confirmed = useFetch(buildVerificationsPath({ ...base, status: 'Confirmed' }));
  const reopened = useFetch(buildVerificationsPath({ ...base, status: 'Reopened' }));
  const escalated = useFetch(buildVerificationsPath({ ...base, status: 'Escalated' }));
  const expired = useFetch(buildVerificationsPath({ ...base, status: 'Expired' }));

  const count = (result) => (result.isLoading || result.error ? null : result.data?.totalCount ?? null);

  return {
    '': count(all),
    Pending: count(pending),
    AwaitingReporterResponse: count(awaiting),
    Confirmed: count(confirmed),
    Reopened: count(reopened),
    Escalated: count(escalated),
    Expired: count(expired),
  };
}

export default useVerificationStatusCounts;
