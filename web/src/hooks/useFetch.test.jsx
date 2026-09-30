import { renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { API_BASE_URL, SESSION_EXPIRED_MESSAGE } from '../services/apiClient';
import { getToken, setToken } from '../services/tokenStore';
import { jsonResponse, stubFetch } from '../test/http';
import useFetch from './useFetch';

// API integration: what useFetch sends, and how each kind of answer becomes
// { data, isLoading, error } — the three states every page renders.
describe('useFetch', () => {
  it('starts loading, then returns the body from the API it was given a path on', async () => {
    const fetch = stubFetch(() => jsonResponse({ id: 7 }));

    const { result } = renderHook(() => useFetch('/api/assets/7'));

    expect(result.current.isLoading).toBe(true);
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.data).toEqual({ id: 7 });
    expect(result.current.error).toBeNull();
    expect(fetch.mock.calls[0][0]).toBe(`${API_BASE_URL}/api/assets/7`);
  });

  it('sends the stored token as a Bearer header', async () => {
    setToken('token-abc');
    const fetch = stubFetch(() => jsonResponse({}));

    const { result } = renderHook(() => useFetch('/api/reports'));
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(fetch.mock.calls[0][1].headers.Authorization).toBe('Bearer token-abc');
  });

  it('treats a 401 on a request that carried a token as an expired session, and drops the token', async () => {
    setToken('stale-token');
    stubFetch(() => jsonResponse({}, 401));

    const { result } = renderHook(() => useFetch('/api/reports'));
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.error.status).toBe(401);
    expect(result.current.error.message).toBe(SESSION_EXPIRED_MESSAGE);
    expect(getToken()).toBeNull();
  });

  it('reports a 403 as a permission problem, not a session problem, and keeps the token', async () => {
    setToken('valid-token');
    stubFetch(() => jsonResponse({}, 403));

    const { result } = renderHook(() => useFetch('/api/workorders/approvals'));
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.error.status).toBe(403);
    expect(result.current.error.message).toMatch(/permission/i);
    expect(getToken()).toBe('valid-token');
  });

  it('turns a network failure into "Could not reach the API." rather than throwing', async () => {
    stubFetch(() => {
      throw new TypeError('Failed to fetch');
    });

    const { result } = renderHook(() => useFetch('/api/assets'));
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.data).toBeNull();
    expect(result.current.error.message).toBe('Could not reach the API.');
  });
});
