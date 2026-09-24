import useFetch from '../../../hooks/useFetch';
import { TECHNICIANS_PATH } from '../services/workOrdersApi';

/**
 * Every Technician, by name, from GET /api/users?role=Technician — the picker behind
 * assigning an order and filtering the board by who is going.
 *
 * FacilitiesManager only on the API, so only components rendered for a manager call this:
 * anyone else would get a 403 for a picker they have no use for.
 */
export function useTechnicians() {
  return useFetch(TECHNICIANS_PATH);
}

export default useTechnicians;
