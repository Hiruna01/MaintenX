import useFetch from '../../../hooks/useFetch';
import { buildReportsPath } from '../services/reportsApi';

/**
 * One page of reports from GET /api/reports.
 *
 * Thin on purpose: the service turns the page's filter state into a path and the shared
 * useFetch does the rest, so the page still gets `{ data, isLoading, error }` and renders
 * all three. `data` is the API's PagedResult: `{ items, page, pageSize, totalCount, totalPages }`.
 */
export function useReports(query) {
  return useFetch(buildReportsPath(query));
}

export default useReports;
