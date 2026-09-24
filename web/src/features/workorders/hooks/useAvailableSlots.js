import useFetch from '../../../hooks/useFetch';
import { buildAvailableSlotsPath } from '../services/workOrdersApi';

/**
 * Free blocks of time for one search from GET /api/workorders/slots/available — at most
 * twenty, earliest first, computed in C# against the room's timetable (and the technician's
 * other visits, when one is named). The component that calls this is mounted once per
 * search, so each search is a fresh request.
 */
export function useAvailableSlots(query) {
  return useFetch(buildAvailableSlotsPath(query));
}

export default useAvailableSlots;
