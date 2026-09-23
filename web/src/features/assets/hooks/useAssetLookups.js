import useFetch from '../../../hooks/useFetch';
import { CATEGORIES_PATH, ROOMS_PATH } from '../services/assetsApi';

/**
 * The categories and rooms every asset screen needs — for the filter pickers, the form
 * pickers, and to turn the list's `assetCategoryId` / `roomId` into names.
 *
 * Both lists are short and unpaginated, so they are fetched whole. The result folds the two
 * requests into one set of states: loading until both are in, an error if either failed.
 */
export function useAssetLookups() {
  const categories = useFetch(CATEGORIES_PATH);
  const rooms = useFetch(ROOMS_PATH);

  return {
    categories: categories.data ?? [],
    rooms: rooms.data ?? [],
    isLoading: categories.isLoading || rooms.isLoading,
    error: categories.error ?? rooms.error,
  };
}

export default useAssetLookups;
