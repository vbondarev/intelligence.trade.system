import { describe, expect, it } from 'vitest';
import { jsonResponse, mockFetch, stubLocationAssign } from '../test/fetchMock';
import { AuthApiError, fetchSession, LOGIN_URL, logout, startLogin } from './authApi';

describe('authApi', () => {
  it('reads anonymous session without credentials leaving the origin', async () => {
    const requests = mockFetch({ '/bff/auth/session': () => jsonResponse({ authenticated: false }) });

    await expect(fetchSession()).resolves.toEqual({ authenticated: false });
    expect(requests[0]?.init?.credentials).toBe('same-origin');
    expect(requests[0]?.init?.cache).toBe('no-store');
  });

  it('keeps only public user fields from the session response', async () => {
    mockFetch({
      '/bff/auth/session': () =>
        jsonResponse({
          authenticated: true,
          user: { userId: 'user-1', subject: 'user-1', access_token: 'leak' },
          refresh_token: 'leak',
        }),
    });

    const session = await fetchSession();

    expect(session).toEqual({ authenticated: true, user: { userId: 'user-1', subject: 'user-1' } });
    expect(JSON.stringify(session)).not.toMatch(/token/i);
  });

  it('rejects failed and malformed session responses', async () => {
    mockFetch({ '/bff/auth/session': () => jsonResponse({}, 503) });
    await expect(fetchSession()).rejects.toBeInstanceOf(AuthApiError);

    mockFetch({ '/bff/auth/session': () => jsonResponse({ authenticated: true }) });
    await expect(fetchSession()).rejects.toBeInstanceOf(AuthApiError);
  });

  it('starts login only through the BFF endpoint without prompt=login', () => {
    const assign = stubLocationAssign();

    startLogin();

    expect(assign).toHaveBeenCalledWith('/bff/auth/login?returnUrl=/app');
    expect(LOGIN_URL).not.toContain('prompt');
  });

  it('logs out with antiforgery token first, then follows the same-origin redirect', async () => {
    const assign = stubLocationAssign();
    const requests = mockFetch({
      '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
      '/bff/auth/logout': () => jsonResponse({ redirectUrl: '/bff/auth/logout/complete' }),
    });

    await logout();

    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery', '/bff/auth/logout']);
    expect(requests[1]?.init?.method).toBe('POST');
    expect(new Headers(requests[1]?.init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
    expect(assign).toHaveBeenCalledWith('/bff/auth/logout/complete');
  });

  it.each(['https://evil.example/logout', '//evil.example/logout', '/\\evil.example'])(
    'refuses a logout redirect outside the origin: %s',
    async (redirectUrl) => {
      const assign = stubLocationAssign();
      mockFetch({
        '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
        '/bff/auth/logout': () => jsonResponse({ redirectUrl }),
      });

      await expect(logout()).rejects.toBeInstanceOf(AuthApiError);
      expect(assign).not.toHaveBeenCalled();
    },
  );

  it('does not post logout when the antiforgery token cannot be obtained', async () => {
    const requests = mockFetch({ '/bff/auth/antiforgery': () => jsonResponse({}, 401) });

    await expect(logout()).rejects.toBeInstanceOf(AuthApiError);
    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery']);
  });

  it('reports a rejected logout request', async () => {
    mockFetch({
      '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
      '/bff/auth/logout': () => jsonResponse({}, 400),
    });

    await expect(logout()).rejects.toBeInstanceOf(AuthApiError);
  });
});
