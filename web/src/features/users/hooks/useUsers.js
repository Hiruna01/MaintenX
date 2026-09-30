import useFetch from '../../../hooks/useFetch';
import { buildUsersPath } from '../services/usersApi';

/**
 * One page of accounts from GET /api/users (Admin only). `data` is the API's PagedResult:
 * `{ items, page, pageSize, totalCount, totalPages }`.
 */
export function useUsers(query) {
  return useFetch(buildUsersPath(query));
}

export default useUsers;
