import useFetch from '../../../hooks/useFetch';
import { buildVerificationsPath } from '../services/verificationApi';

/**
 * One page of checks from GET /api/verifications. `data` is the API's PagedResult:
 * `{ items, page, pageSize, totalCount, totalPages }`.
 */
export function useVerifications(query) {
  return useFetch(buildVerificationsPath(query));
}

export default useVerifications;
