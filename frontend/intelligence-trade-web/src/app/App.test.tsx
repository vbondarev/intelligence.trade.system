import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider } from '../auth/AuthProvider';
import type { AuthState } from '../auth/authTypes';
import { jsonResponse, mockFetch, stubLocationAssign } from '../test/fetchMock';
import { AppRoutes } from './router';

const authenticatedSession = {
  authenticated: true,
  user: { userId: '7a0c8b5e-6c1d-4f3e-9a6b-2d4e8f1a3c5b', subject: '7a0c8b5e-6c1d-4f3e-9a6b-2d4e8f1a3c5b' },
};

const noConnections = { '/bff/me/exchange-accounts': () => jsonResponse({ items: [] }) };

function authRequestUrls(requests: { url: string }[]): string[] {
  return requests.map((request) => request.url).filter((url) => url.startsWith('/bff/auth/'));
}

function renderAt(path: string) {
  return render(
    <AuthProvider>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes />
      </MemoryRouter>
    </AuthProvider>,
  );
}

describe('App routing and session bootstrap', () => {
  it('shows loading state while the session is being checked', () => {
    mockFetch({ '/bff/auth/session': () => new Promise<Response>(() => {}) });

    renderAt('/');

    expect(screen.getByRole('status')).toHaveTextContent('Проверяем сессию');
  });

  it('shows the public landing for an anonymous visitor', async () => {
    mockFetch({ '/bff/auth/session': () => jsonResponse({ authenticated: false }) });

    renderAt('/');

    expect(await screen.findByRole('button', { name: 'Войти' })).toBeVisible();
  });

  it('redirects an anonymous visitor from /app to the landing', async () => {
    mockFetch({ '/bff/auth/session': () => jsonResponse({ authenticated: false }) });

    renderAt('/app');

    expect(await screen.findByRole('button', { name: 'Войти' })).toBeVisible();
    expect(screen.queryByRole('navigation')).not.toBeInTheDocument();
  });

  it('shows the authenticated shell on /app', async () => {
    mockFetch({ '/bff/auth/session': () => jsonResponse(authenticatedSession), ...noConnections });

    renderAt('/app');

    expect(await screen.findByRole('heading', { name: 'Обзор' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Выйти' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'Обзор' })).toHaveAttribute('href', '/app');
  });

  it('moves an authenticated visitor from the landing to /app', async () => {
    mockFetch({ '/bff/auth/session': () => jsonResponse(authenticatedSession), ...noConnections });

    renderAt('/');

    expect(await screen.findByRole('heading', { name: 'Обзор' })).toBeVisible();
  });

  it('starts login through the BFF endpoint from the landing', async () => {
    const assign = stubLocationAssign();
    mockFetch({ '/bff/auth/session': () => jsonResponse({ authenticated: false }) });
    renderAt('/');

    await userEvent.click(await screen.findByRole('button', { name: 'Войти' }));

    expect(assign).toHaveBeenCalledWith('/bff/auth/login?returnUrl=/app');
  });

  it.each([403, 503])('offers full logout when session bootstrap returns %s', async (status) => {
    const assign = stubLocationAssign();
    const requests = mockFetch({
      '/bff/auth/session': () => jsonResponse({}, status),
      '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
      '/bff/auth/logout': () => jsonResponse({ redirectUrl: '/bff/auth/logout/complete' }),
    });
    renderAt('/');

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось проверить сессию');
    await userEvent.click(screen.getByRole('button', { name: 'Выйти' }));

    expect(requests.map((request) => request.url)).toEqual([
      '/bff/auth/session',
      '/bff/auth/antiforgery',
      '/bff/auth/logout',
    ]);
    expect(assign).toHaveBeenCalledWith('/bff/auth/logout/complete');
  });

  it('does not offer logout when session bootstrap fails without a preserved session', async () => {
    mockFetch({
      '/bff/auth/session': () => {
        throw new Error('network down');
      },
    });
    renderAt('/');

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось проверить сессию');
    expect(screen.getByRole('button', { name: 'Повторить' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Выйти' })).not.toBeInTheDocument();
  });

  it('offers retry after a session bootstrap error and recovers', async () => {
    let attempts = 0;
    mockFetch({
      '/bff/auth/session': () => {
        attempts += 1;
        return attempts === 1 ? jsonResponse({}, 503) : jsonResponse({ authenticated: false });
      },
    });
    renderAt('/');

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось проверить сессию');
    await userEvent.click(screen.getByRole('button', { name: 'Повторить' }));

    expect(await screen.findByRole('button', { name: 'Войти' })).toBeVisible();
    expect(attempts).toBe(2);
  });

  it('logs out through antiforgery, CSRF header and returned redirect', async () => {
    const assign = stubLocationAssign();
    const requests = mockFetch({
      '/bff/auth/session': () => jsonResponse(authenticatedSession),
      '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
      '/bff/auth/logout': () => jsonResponse({ redirectUrl: '/bff/auth/logout/complete' }),
      ...noConnections,
    });
    renderAt('/app');

    await userEvent.click(await screen.findByRole('button', { name: 'Выйти' }));

    expect(authRequestUrls(requests)).toEqual(['/bff/auth/session', '/bff/auth/antiforgery', '/bff/auth/logout']);
    const logout = requests.find((request) => request.url === '/bff/auth/logout');
    expect(new Headers(logout?.init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
    expect(assign).toHaveBeenCalledWith('/bff/auth/logout/complete');
  });

  it('shows an error when logout fails and keeps the shell usable', async () => {
    mockFetch({
      '/bff/auth/session': () => jsonResponse(authenticatedSession),
      '/bff/auth/antiforgery': () => jsonResponse({}, 503),
      ...noConnections,
    });
    renderAt('/app');

    await userEvent.click(await screen.findByRole('button', { name: 'Выйти' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось выйти');
    expect(screen.getByRole('button', { name: 'Выйти' })).toBeVisible();
  });

  it('keeps auth state free of token fields', () => {
    const state: AuthState = {
      loading: false,
      authenticated: true,
      user: authenticatedSession.user,
      error: null,
      logoutAvailable: false,
    };

    expect(Object.keys(state).join(',')).not.toMatch(/token/i);
    expect(Object.keys(state.user ?? {}).join(',')).not.toMatch(/token/i);
  });
});
