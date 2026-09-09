import { useEffect, useState } from 'react';

import {
  ApiError,
  SESSION_EXPIRED_MESSAGE,
  authHeaders,
  buildUrl,
  handleUnauthorized,
} from '../services/apiClient';
import { getToken } from '../services/tokenStore';

/**
 * The lab's useFetch, extended for this project in two ways:
 *   1. it attaches the JWT Authorization header, and
 *   2. it treats a 401 on an authenticated request as a session expiry — the token is
 *      dropped, which AuthProvider observes and turns into a redirect to /login.
 *
 * Returns `{ data, isLoading, error }`. Every page must render all three states; a blank
 * screen while loading is a bug.
 *
 * @param {string} path API-relative path, e.g. `/api/workflows?page=1`.
 */
export function useFetch(path) {
  const [data, setData] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    // Guards every setState below: once the effect is cleaned up (the path changed, or the
    // component unmounted) a late response must not write into a dead component.
    let active = true;

    // Re-entering the loading state is the point: when `path` changes the hook must stop
    // reporting the previous URL's result. Deriving it instead would mean tracking which
    // path the current data belongs to — more machinery than this hook is worth.
    // eslint-disable-next-line react/set-state-in-effect
    setIsLoading(true);
    setError(null);

    const hadToken = getToken() !== null;

    fetch(buildUrl(path), {
      headers: { Accept: 'application/json', ...authHeaders() },
    })
      .then(async (response) => {
        if (response.status === 401) {
          handleUnauthorized(hadToken);
          throw new ApiError(401, hadToken ? SESSION_EXPIRED_MESSAGE : 'You are not signed in.');
        }
        if (response.status === 403) {
          throw new ApiError(403, 'You do not have permission to view this.');
        }
        if (!response.ok) {
          throw new ApiError(response.status, `Request failed with status ${response.status}.`);
        }
        return response.status === 204 ? null : response.json();
      })
      .then((body) => {
        if (!active) return;
        setData(body);
      })
      .catch((err) => {
        if (!active) return;
        setData(null);
        setError(err instanceof ApiError ? err : new Error('Could not reach the API.'));
      })
      .finally(() => {
        if (!active) return;
        setIsLoading(false);
      });

    return () => {
      active = false;
    };
  }, [path]);

  return { data, isLoading, error };
}

export default useFetch;
