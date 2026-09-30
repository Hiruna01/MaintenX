// Stand-ins for the API. Tests stub global fetch with these, so no test can reach a server.
import { vi } from 'vitest';

/** A Response carrying `body` as JSON with `status`. */
export function jsonResponse(body, status = 200) {
  return new Response(body === undefined ? null : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

/** Stubs fetch to answer every call with `responder(url, init)`, and returns the mock. */
export function stubFetch(responder) {
  const mock = vi.fn(async (url, init) => responder(String(url), init ?? {}));
  vi.stubGlobal('fetch', mock);
  return mock;
}

/** An empty PagedResult, the shape every list endpoint answers with. */
export function emptyPage(page = 1, pageSize = 10) {
  return { items: [], page, pageSize, totalCount: 0, totalPages: 0 };
}
