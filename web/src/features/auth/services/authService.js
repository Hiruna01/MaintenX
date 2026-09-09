/** Every call the auth feature makes to the API lives here — components never fetch. */

import { request } from '../../../services/apiClient';
import { clearToken, setToken } from '../../../services/tokenStore';

/**
 * POST /api/auth/login -> 200 with { token, userId, email, fullName, role }, or 401.
 *
 * Any stale token is dropped first: without that, a request carrying an expired token
 * would come back 401 and be reported as "session expired" rather than "wrong password".
 */
export async function login(email, password) {
  clearToken();

  const auth = await request('/api/auth/login', {
    method: 'POST',
    body: { email, password },
  });

  setToken(auth.token);

  return {
    id: auth.userId,
    email: auth.email,
    fullName: auth.fullName,
    role: auth.role,
  };
}

/** GET /api/auth/me -> 200 with { id, email, fullName, role }. Used to restore a session. */
export function fetchCurrentUser() {
  return request('/api/auth/me');
}

/** No server call: the API issues an access token only, with nothing to revoke. */
export function logout() {
  clearToken();
}
