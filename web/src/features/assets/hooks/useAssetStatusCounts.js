import useFetch from '../../../hooks/useFetch';
import { buildAssetsPath } from '../services/assetsApi';

/**
 * How many assets each status tab would show, under the other filters currently applied.
 * One page-of-one request per tab; the count is the API's `totalCount`, never a tally of the
 * page already fetched. A count that has not arrived (or failed) is null and renders as "·".
 */
export function useAssetStatusCounts({ search, categoryId, roomId }) {
  const base = { search, categoryId, roomId, page: 1, pageSize: 1 };
  const all = useFetch(buildAssetsPath({ ...base, status: '' }));
  const active = useFetch(buildAssetsPath({ ...base, status: 'Active' }));
  const maintenance = useFetch(buildAssetsPath({ ...base, status: 'UnderMaintenance' }));
  const retired = useFetch(buildAssetsPath({ ...base, status: 'Retired' }));

  const count = (result) => (result.isLoading || result.error ? null : result.data?.totalCount ?? null);

  return {
    '': count(all),
    Active: count(active),
    UnderMaintenance: count(maintenance),
    Retired: count(retired),
  };
}

export default useAssetStatusCounts;
