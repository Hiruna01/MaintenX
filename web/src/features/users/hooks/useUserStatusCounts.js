import useFetch from '../../../hooks/useFetch';
import { USER_STATUSES, buildUsersPath } from '../services/usersApi';

/**
 * How many accounts each status tab would show, under the search and role currently applied.
 * One page-of-one request per tab; the count is the API's `totalCount`, never a tally of the
 * page already fetched. A count that has not arrived (or failed) is null and renders as "·".
 */
export function useUserStatusCounts({ search, role }) {
  const base = { search, role, page: 1, pageSize: 1 };
  const all = useFetch(buildUsersPath({ ...base, status: USER_STATUSES.All }));
  const active = useFetch(buildUsersPath({ ...base, status: USER_STATUSES.Active }));
  const deactivated = useFetch(buildUsersPath({ ...base, status: USER_STATUSES.Deactivated }));

  const count = (result) => (result.isLoading || result.error ? null : result.data?.totalCount ?? null);

  return {
    [USER_STATUSES.All]: count(all),
    [USER_STATUSES.Active]: count(active),
    [USER_STATUSES.Deactivated]: count(deactivated),
  };
}

export default useUserStatusCounts;
