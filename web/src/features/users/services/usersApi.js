/** Every call the user management feature makes to the API lives here — components never fetch. */

import { request } from '../../../services/apiClient';
import { ROLES } from '../../auth/services/roles';

/**
 * Every member of the API's `Role` enum, by NAME, in the order the pickers show them. Built
 * from `ROLES` so there is one copy of the names in the client.
 */
export const ROLE_OPTIONS = [ROLES.Reporter, ROLES.Technician, ROLES.FacilitiesManager, ROLES.Admin];

/**
 * The status tabs. `isActive` is a boolean on the API, so the tab values are the strings the
 * query string carries — '' for both.
 */
export const USER_STATUSES = {
  All: '',
  Active: 'true',
  Deactivated: 'false',
};

export const DEFAULT_PAGE_SIZE = 10;

/** Matches the DataAnnotations on CreateUserDto / UpdateUserDto / ResetPasswordDto. */
export const FIELD_LIMITS = {
  email: 256,
  fullName: 200,
  passwordMin: 8,
  passwordMax: 128,
};

/**
 * Builds the path for GET /api/users (Admin only) — read through useFetch, which owns loading,
 * error and the JWT. Empty filters are left out rather than sent blank; `status` is one of
 * USER_STATUSES and travels as `isActive`.
 */
export function buildUsersPath({ search = '', role = '', status = '', page = 1, pageSize = DEFAULT_PAGE_SIZE } = {}) {
  const params = new URLSearchParams();
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));

  if (search.trim()) params.set('search', search.trim());
  if (role) params.set('role', role);
  if (status) params.set('isActive', status);

  return `/api/users?${params.toString()}`;
}

/** GET /api/users/{id} — one account, with whether it is active and its live work order count. */
export function buildUserPath(id) {
  return `/api/users/${encodeURIComponent(id)}`;
}

/** POST /api/users -> 201 with the new account. A 409 means the email is taken. */
export function createUser(values) {
  return request('/api/users', {
    method: 'POST',
    body: {
      email: values.email.trim(),
      fullName: values.fullName.trim(),
      password: values.password,
      role: values.role,
    },
  });
}

/** PUT /api/users/{id} -> 204. Name, email and role only — never a password or IsActive. */
export function updateUser(id, values) {
  return request(buildUserPath(id), {
    method: 'PUT',
    body: {
      email: values.email.trim(),
      fullName: values.fullName.trim(),
      role: values.role,
    },
  });
}

/**
 * DELETE /api/users/{id} -> 204. Deactivates: the account can no longer sign in, and its
 * reports and work stay exactly where they are. A 409 is the API refusing — your own account,
 * or a Technician with unfinished work — and is shown as it was worded.
 */
export function deactivateUser(id) {
  return request(buildUserPath(id), { method: 'DELETE' });
}

/** POST /api/users/{id}/reactivate -> 204. */
export function reactivateUser(id) {
  return request(`${buildUserPath(id)}/reactivate`, { method: 'POST' });
}

/** POST /api/users/{id}/password -> 204. The Admin sets a temporary password. */
export function resetUserPassword(id, password) {
  return request(`${buildUserPath(id)}/password`, { method: 'POST', body: { password } });
}
