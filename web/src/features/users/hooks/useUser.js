import useFetch from '../../../hooks/useFetch';
import { buildUserPath } from '../services/usersApi';

/** One account from GET /api/users/{id}. */
export function useUser(id) {
  return useFetch(buildUserPath(id));
}

export default useUser;
