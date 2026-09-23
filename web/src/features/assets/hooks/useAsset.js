import useFetch from '../../../hooks/useFetch';
import { buildAssetPath } from '../services/assetsApi';

/**
 * One asset from GET /api/assets/{id}: its category, its room, and the full service
 * history OLDEST FIRST — the order a repeat failure reads as one. Nothing on the client
 * re-sorts it.
 */
export function useAsset(id) {
  return useFetch(buildAssetPath(id));
}

export default useAsset;
