import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import { AuthProvider } from '../auth/AuthProvider';
import { useAuth } from '../auth/authContext';
import type { AuthState } from '../auth/authTypes';
import { jsonResponse, mockFetch, stubLocationAssign } from '../test/fetchMock';
import { SessionRecheck } from '../test/SessionRecheck';
import { emitRealtime } from '../test/realtimeTestDriver';
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
    { wrapper: MantineProvider },
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

  it.each([403, 503])('обрабатывает статус %s при проверке доступа на паузе', async (status) => {
    let recovering = false;
    const assign = stubLocationAssign();
    const requests = mockFetch({
      '/bff/auth/session': () => recovering ? jsonResponse({}, status) : jsonResponse(authenticatedSession),
      '/bff/me/exchange-accounts': () => recovering ? jsonResponse({}, 403) : jsonResponse({ items: [] }),
      '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
      '/bff/auth/logout': () => jsonResponse({ redirectUrl: '/bff/auth/logout/complete' }),
    });
    renderAt('/app/settings/connections');
    const heading = await screen.findByRole('heading', { name: 'Подключения', level: 1 });
    await userEvent.click(screen.getByRole('button', { name: 'Приостановить автообновление' }));
    recovering = true;
    await act(async () => emitRealtime({ type: 'recovery' }));
    await waitFor(() => expect(authRequestUrls(requests).filter((url) => url === '/bff/auth/session')).toHaveLength(2));

    if (status === 403) {
      expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось проверить сессию');
      expect(heading).not.toBeInTheDocument();
      await userEvent.click(screen.getByRole('button', { name: 'Выйти' }));
      expect(assign).toHaveBeenCalledWith('/bff/auth/logout/complete');
    } else {
      expect(heading).toBeVisible();
      expect(screen.getByRole('button', { name: 'Возобновить автообновление' })).toBeVisible();
    }
  });

  it.each([false, true])('игнорирует старый 401 при перепроверке сессии (перепроверка завершена: %s)', async (confirmed) => {
    const assign = stubLocationAssign();
    let finishOldRead!: (response: Response) => void;
    const oldRead = new Promise<Response>((resolve) => { finishOldRead = resolve; });
    let finishCheck!: (response: Response) => void;
    const check = new Promise<Response>((resolve) => { finishCheck = resolve; });
    let sessionReads = 0;
    let accountReads = 0;
    const requests = mockFetch({
      '/bff/auth/session': () => ++sessionReads === 1 ? jsonResponse(authenticatedSession) : check,
      '/bff/me/exchange-accounts': () => {
        accountReads += 1;
        if (accountReads === 1) return oldRead;
        return new Response(null, { status: accountReads === 2 ? 403 : 401 });
      },
    });
    renderAt('/app');
    const heading = await screen.findByRole('heading', { name: 'Обзор' });
    await waitFor(() => expect(accountReads).toBe(1));
    await userEvent.click(screen.getByRole('button', { name: 'Приостановить автообновление' }));
    await act(async () => emitRealtime({ type: 'recovery' }));
    await waitFor(() => expect(authRequestUrls(requests).filter((url) => url === '/bff/auth/session')).toHaveLength(2));

    if (confirmed) await act(async () => finishCheck(jsonResponse(authenticatedSession)));
    await act(async () => finishOldRead(new Response(null, { status: 401 })));
    expect(heading).toBeVisible();
    if (!confirmed) await act(async () => finishCheck(jsonResponse(authenticatedSession)));

    await userEvent.click(screen.getByRole('button', { name: 'Возобновить автообновление' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Войти снова' }));
    expect(heading).not.toBeInTheDocument();
    expect(assign).toHaveBeenCalledWith('/bff/auth/login?returnUrl=%2Fapp');
  });

  it('сохраняет снимок на паузе после запоздалого 403 прежней эпохи', async () => {
    let finish!: (response: Response) => void;
    const pending = new Promise<Response>((resolve) => { finish = resolve; });
    let finishCheck!: (response: Response) => void;
    const accessCheck = new Promise<Response>((resolve) => { finishCheck = resolve; });
    let reads = 0;
    let checks = 0;
    const account = {
      id: '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6', displayName: 'Контрольное подключение',
      exchange: 'bybit', connectionStatus: 'connected', capabilities: [], lastSyncedAt: null,
    };
    const requests = mockFetch({
      '/bff/auth/session': () => ++checks === 3 ? accessCheck : jsonResponse(authenticatedSession),
      '/bff/me/exchange-accounts': () => {
        reads += 1;
        if (reads === 1) return jsonResponse({ items: [account] });
        return reads === 2 ? pending : new Response(null, { status: 403 });
      },
    });
    render(
      <AuthProvider>
        <MemoryRouter initialEntries={['/app/settings/connections']}>
          <AppRoutes />
          <SessionRecheck userId={authenticatedSession.user.userId} />
        </MemoryRouter>
      </AuthProvider>,
      { wrapper: MantineProvider },
    );

    const name = await screen.findByText(account.displayName);
    await userEvent.click(screen.getByRole('button', { name: 'Приостановить автообновление' }));
    await act(async () => emitRealtime({ type: 'recovery' }));
    expect(reads).toBe(2);
    await userEvent.click(screen.getByRole('button', { name: 'Проверить сессию' }));
    expect(authRequestUrls(requests).filter((url) => url === '/bff/auth/session')).toHaveLength(2);
    await act(async () => finish(new Response(null, { status: 403 })));

    expect(name).toBeVisible();
    expect(authRequestUrls(requests).filter((url) => url === '/bff/auth/session')).toHaveLength(2);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();

    await act(async () => emitRealtime({ type: 'recovery' }));
    expect(name).not.toBeInTheDocument();
    expect(checks).toBe(3);
    await act(async () => emitRealtime({ type: 'recovery' }));
    expect(reads).toBe(4);
    expect(checks).toBe(3);
    await act(async () => finishCheck(jsonResponse(authenticatedSession)));
  });

  it('перекрывающиеся проверки сохраняют reconnect подтверждённого пользователя', async () => {
    let finishFirst!: (response: Response) => void;
    const first = new Promise<Response>((resolve) => { finishFirst = resolve; });
    let finishSecond!: (response: Response) => void;
    const second = new Promise<Response>((resolve) => { finishSecond = resolve; });
    let reads = 0;
    mockFetch({
      '/bff/auth/session': () => [jsonResponse(authenticatedSession), first, second][reads++]!,
    });
    let revalidate!: ReturnType<typeof useAuth>['revalidate'];
    function Probe() {
      const auth = useAuth();
      revalidate = auth.revalidate;
      return <p>{auth.user?.subject}</p>;
    }
    render(<AuthProvider><Probe /></AuthProvider>);
    await screen.findByText(authenticatedSession.user.subject);
    const check = () => revalidate(authenticatedSession.user.userId, new AbortController().signal);
    const reconnectCheck = check();
    const accessCheck = check();

    await act(async () => finishSecond(jsonResponse(authenticatedSession)));
    await expect(accessCheck).resolves.toBe(true);
    await act(async () => finishFirst(new Response(null, { status: 403 })));
    await expect(reconnectCheck).resolves.toBe(true);
    expect(screen.getByText(authenticatedSession.user.subject)).toBeVisible();
  });

  it.each(['отмена', 'смена пользователя'])('игнорирует поздний 403: %s', async (reason) => {
    const check = new AbortController();
    const nextUser = { userId: 'other-user', subject: 'Другой пользователь' };
    let finishCheck!: (response: Response) => void;
    const pending = new Promise<Response>((resolve) => { finishCheck = resolve; });
    let sessionReads = 0;
    const requests = mockFetch({
      '/bff/auth/session': () => {
        sessionReads += 1;
        if (sessionReads === 2) return pending;
        return jsonResponse(sessionReads === 1 ? authenticatedSession : { authenticated: true, user: nextUser });
      },
    });
    function Probe() {
      const auth = useAuth();
      return (
        <>
          <p>{auth.user?.subject}</p>
          <button type="button" onClick={() => void auth.revalidate(authenticatedSession.user.userId, check.signal)}>Проверить</button>
          <button type="button" onClick={auth.retry}>Загрузить сессию</button>
        </>
      );
    }
    render(<AuthProvider><Probe /></AuthProvider>);
    await screen.findByText(authenticatedSession.user.subject);
    await userEvent.click(screen.getByRole('button', { name: 'Проверить' }));
    expect(requests).toHaveLength(2);
    if (reason === 'отмена') check.abort();
    else {
      await userEvent.click(screen.getByRole('button', { name: 'Загрузить сессию' }));
      await screen.findByText(nextUser.subject);
    }
    await act(async () => finishCheck(jsonResponse({}, 403)));
    expect(screen.getByText(reason === 'отмена' ? authenticatedSession.user.subject : nextUser.subject)).toBeVisible();
  });

  it.each(['authenticated', 'anonymous', 'failed'] as const)(
    'повторно проверяет сессию после истечения: %s',
    async (outcome) => {
      let timingExpired = false;
      let verifyAuthPending = false;
      let completeAuth!: (response: Response) => void;
      let failAuth!: (error: Error) => void;
      const pendingAuth = new Promise<Response>((resolve, reject) => {
        completeAuth = resolve;
        failAuth = reject;
      });
      const requests = mockFetch({
        ...noConnections,
        '/bff/auth/session': () => verifyAuthPending ? pendingAuth : jsonResponse(authenticatedSession),
        '/bff/auth/session/timing': () => timingExpired
          ? jsonResponse(null, 401)
          : jsonResponse({ serverTimeUtc: '2026-11-19T12:00:00Z', expiresAtUtc: '2026-11-19T12:30:00Z' }),
      });
      const reads = () => requests.filter(({ url }) => url === '/bff/me/exchange-accounts').length;
      renderAt('/app/settings/connections');
      await screen.findByRole('heading', { name: 'Подключения' });
      await waitFor(() => expect(reads()).toBeGreaterThan(0));
      const initialReads = reads();

      timingExpired = true;
      await act(async () => { window.dispatchEvent(new Event('focus')); });
      await screen.findByRole('dialog', { name: 'Сессия завершена' });
      expect(screen.queryByRole('heading', { name: 'Подключения' })).not.toBeInTheDocument();

      timingExpired = false;
      // Новый timing действителен, но auth boundary ещё должна проверить идентичность.
      const nextSession = requests.filter(({ url }) => url === '/bff/auth/session').length;
      verifyAuthPending = true;
      await act(async () => { window.dispatchEvent(new Event('focus')); });
      await waitFor(() => expect(requests.filter(({ url }) => url === '/bff/auth/session').length).toBe(nextSession + 1));
      expect(screen.getByRole('status')).toHaveTextContent('Проверяем сессию');
      expect(reads()).toBe(initialReads);

      await act(async () => {
        if (outcome === 'failed') failAuth(new Error('Сеть недоступна'));
        else completeAuth(jsonResponse(outcome === 'authenticated' ? authenticatedSession : { authenticated: false }));
      });
      if (outcome === 'authenticated') {
        await screen.findByRole('heading', { name: 'Подключения' });
        await waitFor(() => expect(reads()).toBeGreaterThan(initialReads));
      } else {
        if (outcome === 'failed') {
          expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось проверить сессию');
          expect(screen.getByRole('button', { name: 'Повторить' })).toBeVisible();
        } else {
          await screen.findByRole('button', { name: 'Войти' });
        }
        expect(screen.queryByRole('heading', { name: 'Подключения' })).not.toBeInTheDocument();
        expect(reads()).toBe(initialReads);
      }
    },
  );

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

    expect(authRequestUrls(requests)).toEqual([
      '/bff/auth/session',
      '/bff/auth/session/timing',
      '/bff/auth/antiforgery',
      '/bff/auth/logout',
    ]);
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
import { MantineProvider } from '@mantine/core';
