import useFetch from '../../../hooks/useFetch';
import { buildAssetsPath } from '../../assets/services/assetsApi';

/** Every asset in one room fits on a page: the API's own cap, so nothing is paged away. */
const ROOM_ASSETS_PAGE_SIZE = 100;

/** The assets registered in one room — what a work order raised from a report there can name. */
export function useRoomAssets(roomId) {
  return useFetch(buildAssetsPath({ roomId, pageSize: ROOM_ASSETS_PAGE_SIZE }));
}

export default useRoomAssets;
