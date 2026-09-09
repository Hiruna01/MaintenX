/**
 * The one place that knows the API's base URL and how a request is authenticated.
 *
 * The React app talks ONLY to the ASP.NET Core API — never to the Python agent service.
 * Feature services call `request()`; pages and components never call fetch directly.
 */

import { expireSession, getToken } from './tokenStore';

export const API_BASE_URL = (
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5138'
).replace(/\/$/, '');

/** An error carrying the HTTP status, so callers can tell 401 from 403 from 404. */
export class ApiError extends Error {
  constructor(status, message) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

export const SESSION_EXPIRED_MESSAGE = 'Your session has expired. Please sign in again.';

/** Accepts an absolute URL or an API-relative path such as `/api/workflows?page=1`. */
export function buildUrl(path) {
  if (/^https?:\/\//i.test(path)) {
    return path;
  }
  return `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`;
}

export function authHeaders() {
  const token = getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

/**
 * A 401 means one of two things and they are handled differently:
 *   - we sent a token and it was rejected  -> the session expired, sign the user out
 *   - we sent no token at all              -> the caller (e.g. login) gets a plain 401
 */
export function handleUnauthorized(hadToken) {
  if (hadToken) {
    expireSession();
  }
}

/**
 * Reads a ProblemDetails / ValidationProblemDetails body if the API sent one, so the user
 * sees the API's own message rather than "Request failed".
 */
async function readErrorMessage(response) {
  try {
    const body = await response.json();
    if (body?.errors && typeof body.errors === 'object') {
      const first = Object.values(body.errors).flat()[0];
      if (first) return first;
    }
    if (body?.detail) return body.detail;
    if (body?.title) return body.title;
  } catch {
    // Empty or non-JSON body — fall through to the generic message below.
  }
  return `Request failed with status ${response.status}.`;
}

/**
 * Performs one request and returns the parsed body, or throws an ApiError.
 * 204 No Content comes back as null.
 */
export async function request(path, { method = 'GET', body, signal } = {}) {
  const hadToken = getToken() !== null;

  const response = await fetch(buildUrl(path), {
    method,
    signal,
    headers: {
      Accept: 'application/json',
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...authHeaders(),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (response.status === 401) {
    handleUnauthorized(hadToken);
    throw new ApiError(401, hadToken ? SESSION_EXPIRED_MESSAGE : 'Incorrect email or password.');
  }

  if (!response.ok) {
    throw new ApiError(response.status, await readErrorMessage(response));
  }

  if (response.status === 204) {
    return null;
  }

  return response.json();
}
