import useFetch from '../../../hooks/useFetch';
import { buildAssetsPath } from '../services/assetsApi';

/**
 * One page of the asset catalogue from GET /api/assets.
 *
 * Thin on purpose: the service turns the page's filter state into a path and the shared
 * useFetch does the rest, so the page still gets `{ data, isLoading, error }` and renders
 * all three. `data` is the API's PagedResult: `{ items, page, pageSize, totalCount, totalPages }`.
 */
export function useAssets(query) {
  return useFetch(buildAssetsPath(query));
}

export default useAssets;
