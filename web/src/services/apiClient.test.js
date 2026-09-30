import { describe, expect, it } from 'vitest';

import { jsonResponse, stubFetch } from '../test/http';
import { request } from './apiClient';
import { getToken, setToken } from './tokenStore';

// API integration for writes: what request() sends, and how it reads the API's errors.
describe('apiClient.request', () => {
  it('sends a JSON body with its content type and the Bearer token', async () => {
    setToken('token-xyz');
    const fetch = stubFetch(() => jsonResponse({ id: 1 }, 201));

    const created = await request('/api/assets', { method: 'POST', body: { name: 'Projector' } });

    const [, init] = fetch.mock.calls[0];
    expect(created).toEqual({ id: 1 });
    expect(init.method).toBe('POST');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(init.headers.Authorization).toBe('Bearer token-xyz');
    expect(JSON.parse(init.body)).toEqual({ name: 'Projector' });
  });

  it('shows the first field error of a 400 ValidationProblem, as the API worded it', async () => {
    stubFetch(() =>
      jsonResponse({ title: 'One or more validation errors occurred.', errors: { AssetTag: ['Asset tag is required.'] } }, 400),
    );

    await expect(request('/api/assets', { method: 'POST', body: {} })).rejects.toMatchObject({
      status: 400,
      message: 'Asset tag is required.',
    });
  });

  it("shows a 409's ProblemDetails detail", async () => {
    stubFetch(() => jsonResponse({ title: 'Conflict', detail: 'Tag PRJ-1 is already in use.' }, 409));

    await expect(request('/api/assets', { method: 'POST', body: {} })).rejects.toMatchObject({
      status: 409,
      message: 'Tag PRJ-1 is already in use.',
    });
  });

  it('reads a 401 with NO token as a failed sign-in, not an expired session', async () => {
    stubFetch(() => jsonResponse({}, 401));

    await expect(request('/api/auth/login', { method: 'POST', body: {} })).rejects.toMatchObject({
      status: 401,
      message: 'Incorrect email or password.',
    });
    expect(getToken()).toBeNull();
  });

  it('returns null for a 204', async () => {
    stubFetch(() => new Response(null, { status: 204 }));

    expect(await request('/api/workorders/3/approve', { method: 'POST' })).toBeNull();
  });
});
