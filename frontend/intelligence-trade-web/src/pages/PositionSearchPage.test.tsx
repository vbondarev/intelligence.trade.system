import { AuthContext } from '../auth/authContext';
import type { SessionReadContext } from '../auth/authTypes';
import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { createAutoUpdates } from '../realtime/autoUpdates';
import { AutoUpdatesContext } from '../realtime/autoUpdatesContext';
import { RealtimeContext } from '../realtime/realtimeContext';
import type { RealtimeInvalidation, RealtimeListener } from '../realtime/realtimeTypes';
import { jsonResponse, mockFetch, type RouteHandler } from '../test/fetchMock';
import { ACCOUNT_ID, positionFixture } from '../test/positionFixtures';
import { PositionSearchPage } from './PositionSearchPage';

const empty = () => jsonResponse({ items: [], hasMore: false, nextCursor: null });
const results = (symbol: string, averageEntryPrice = 60000) => jsonResponse({
  items: [positionFixture({ symbol, averageEntryPrice })], hasMore: false, nextCursor: null,
});

function renderSearch(
  routes: Record<string, RouteHandler>,
  captureSessionRead: () => SessionReadContext = () => ({ isCurrent: () => true, endSession: vi.fn() }),
) {
  const requests = mockFetch({
    '/bff/me/exchange-accounts': () => jsonResponse({ items: [] }),
    '/bff/me/positions?trackingState=closed&symbol=BTCUSDT': empty,
    ...routes,
  });
  const updates = createAutoUpdates(captureSessionRead);
  const listeners = new Set<RealtimeListener>();
  const realtime = {
    state: 'ready' as const,
    subscribe(listener: RealtimeListener) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
    retry() {},
  };
  render(
    <MemoryRouter initialEntries={['/app/positions/search?symbol=BTCUSDT']}>
      <AutoUpdatesContext.Provider value={updates}>
        <RealtimeContext.Provider value={realtime}>
          <AuthContext.Provider value={{
            authenticated: true, loading: false, user: null, error: null, logoutAvailable: false,
            login: vi.fn(), logout: vi.fn(), retry: vi.fn(), revalidate: vi.fn(), captureSessionRead, captureSessionEnd: () => captureSessionRead().endSession,
          }}>
            <PositionSearchPage />
          </AuthContext.Provider>
        </RealtimeContext.Provider>
      </AutoUpdatesContext.Provider>
    </MemoryRouter>,
  );
  return {
    requests, updates,
    emit: (event: RealtimeInvalidation) => listeners.forEach((listen) => listen(event)),
  };
}

afterEach(() => {
  vi.unstubAllGlobals();
});

it('при 403 очередной страницы скрывает результаты и проверяет общую сессию', async () => {
  const { updates } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => jsonResponse({
      items: [positionFixture({ symbol: 'BTCUSDT' })], hasMore: true, nextCursor: 'next',
    }),
    '/bff/me/positions?symbol=BTCUSDT&cursor=next': () => new Response(null, { status: 403 }),
  });
  const checkAccess = vi.fn();
  updates.setAccessCheck(checkAccess);

  await userEvent.click(await screen.findByRole('button', { name: 'Загрузить ещё' }));

  expect(await screen.findByRole('alert')).toHaveTextContent('Доступ к поиску недоступен');
  expect(screen.queryByRole('link', { name: /^BTCUSDT/ })).not.toBeInTheDocument();
  expect(checkAccess).toHaveBeenCalledExactlyOnceWith(403, null);
});

it.each(['справочник', 'группа'] as const)('отбрасывает подготовленный снимок после отзыва: %s', async (area) => {
  const accountsUrl = '/bff/me/exchange-accounts';
  const positionsUrl = '/bff/me/positions?symbol=BTCUSDT';
  const account = { id: ACCOUNT_ID, displayName: 'Старое имя', exchange: 'bybit',
    connectionStatus: 'connected', capabilities: [], lastSyncedAt: null };
  let targetReads = 0;
  const accountResult = () => jsonResponse({ items: [account] });
  const positionResult = () => jsonResponse({
    items: [positionFixture()], hasMore: true, nextCursor: 'old',
  });
  const afterRevocation = new Promise<Response>(() => {});
  const readTarget = (response: () => Response) => {
    targetReads += 1;
    if (targetReads < 3) return response();
    return targetReads === 3 ? new Response(null, { status: 403 }) : afterRevocation;
  };
  const { updates, emit } = renderSearch({
    [accountsUrl]: area === 'справочник' ? () => readTarget(accountResult) : accountResult,
    [positionsUrl]: area === 'группа' ? () => readTarget(positionResult) : positionResult,
  });
  let finish!: () => void;
  const pending = new Promise<void>((resolve) => { finish = resolve; });
  const independent = updates.createReader(async (_, view) => {
    if (view.kind === 'resume') await pending;
  });
  const name = area === 'справочник' ? /Старое имя/ : /^BTCUSDT/;
  expect(await screen.findByRole('link', { name })).toBeVisible();
  await act(async () => updates.toggle());
  await act(async () => updates.toggle());
  expect(targetReads).toBe(2);

  await act(async () => emit({ type: 'recovery' }));
  expect(targetReads).toBe(3);
  expect(screen.queryByRole('link', { name })).not.toBeInTheDocument();
  await act(async () => finish());

  expect(updates.getSnapshot().mode).toBe('live');
  expect(screen.queryByRole('link', { name })).not.toBeInTheDocument();
  if (area === 'группа') expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
  independent.dispose();
});

it.each([false, true])('привязывает 401 к старту GET после ручной страницы (смена эпохи перед GET: %s)', async (beforeRead) => {
  let epoch = 0;
  const endSession = vi.fn();
  const capture = () => {
    const captured = epoch;
    return { isCurrent: () => captured === epoch, endSession: () => { if (captured === epoch) endSession(); } };
  };
  let finishPage!: (response: Response) => void;
  let finishRead!: (response: Response) => void;
  const pendingPage = new Promise<Response>((resolve) => { finishPage = resolve; });
  const pendingRead = new Promise<Response>((resolve) => { finishRead = resolve; });
  let reads = 0;
  const { updates } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => ++reads === 1
      ? jsonResponse({ items: [positionFixture()], hasMore: true, nextCursor: 'next' }) : pendingRead,
    '/bff/me/positions?symbol=BTCUSDT&cursor=next': () => pendingPage,
  }, capture);

  await screen.findByRole('link', { name: /^BTCUSDT/ });
  await act(async () => updates.toggle());
  await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
  await act(async () => updates.toggle());
  expect(reads).toBe(1);
  if (beforeRead) epoch += 1;
  await act(async () => finishPage(new Response(null, { status: 503 })));
  await waitFor(() => expect(reads).toBe(2));
  if (!beforeRead) epoch += 1;
  await act(async () => finishRead(new Response(null, { status: 401 })));

  expect(endSession).toHaveBeenCalledTimes(beforeRead ? 1 : 0);
  expect(updates.getSnapshot().mode).toBe(beforeRead ? 'failed' : 'live');
});

it('прекращает чтение снимка при смене эпохи между страницами', async () => {
  let epoch = 0;
  const capture = () => {
    const captured = epoch;
    return { isCurrent: () => captured === epoch, endSession: vi.fn() };
  };
  let finish!: (response: Response) => void;
  const pending = new Promise<Response>((resolve) => { finish = resolve; });
  let reads = 0;
  let continuations = 0;
  const first = (price: number) => jsonResponse({
    items: [positionFixture({ averageEntryPrice: price })], hasMore: true, nextCursor: 'next',
  });
  const { updates } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => ++reads === 1 ? first(60000) : pending,
    '/bff/me/positions?symbol=BTCUSDT&cursor=next': () => {
      continuations += 1;
      return jsonResponse({ items: [positionFixture({ id: 'other', averageEntryPrice: 65000 })], hasMore: false, nextCursor: null });
    },
  }, capture);

  await userEvent.click(await screen.findByRole('button', { name: 'Загрузить ещё' }));
  expect(await screen.findByRole('link', { name: /65\s000/ })).toBeVisible();
  await act(async () => updates.toggle());
  await act(async () => updates.toggle());
  expect(reads).toBe(2);
  epoch += 1;
  await act(async () => finish(first(70000)));

  expect(updates.getSnapshot().mode).toBe('live');
  expect(continuations).toBe(1);
  expect(screen.queryByRole('link', { name: /70\s000/ })).not.toBeInTheDocument();
  expect(screen.getByRole('link', { name: /60\s000/ })).toBeVisible();
});

it('оставляет фокус и DOM поля после поиска с клавиатуры', async () => {
  renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => results('BTCUSDT'),
    '/bff/me/positions?symbol=ETHUSDT': () => results('ETHUSDT'),
    '/bff/me/positions?trackingState=closed&symbol=ETHUSDT': empty,
  });
  const user = userEvent.setup();
  const input = screen.getByRole('textbox', { name: 'Символ' });
  await user.clear(input);
  await user.type(input, 'ETHUSDT{Enter}');
  expect(await screen.findByRole('link', { name: /^ETHUSDT/ })).toBeInTheDocument();
  expect(screen.getByRole('textbox', { name: 'Символ' })).toBe(input);
  expect(input).toHaveFocus();
  await user.tab();
  expect(screen.getByRole('button', { name: 'Найти' })).toHaveFocus();
});

it('отменяет запрос предыдущего символа и игнорирует его поздний ответ', async () => {
  let resolveOld!: (value: Response) => void;
  const late = new Promise<Response>((resolve) => { resolveOld = resolve; });
  const { requests } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => late,
    '/bff/me/positions?symbol=ETHUSDT': () => results('ETHUSDT'),
    '/bff/me/positions?trackingState=closed&symbol=ETHUSDT': empty,
  });
  const user = userEvent.setup();
  const symbol = screen.getByLabelText('Символ');
  await user.clear(symbol);
  await user.type(symbol, 'ETHUSDT');
  await user.click(screen.getByRole('button', { name: 'Найти' }));
  await screen.findByRole('link', { name: /^ETHUSDT/ });
  expect(requests.find(({ url }) => url === '/bff/me/positions?symbol=BTCUSDT')?.init?.signal?.aborted).toBe(true);
  await act(async () => resolveOld(results('BTCUSDT')));
  expect(screen.queryByRole('link', { name: /^BTCUSDT/ })).not.toBeInTheDocument();
});

it('читает REST при recovery на паузе и публикует новый список при возобновлении', async () => {
  let price = 60000;
  const { requests, updates, emit } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => results('BTCUSDT', price),
  });
  await screen.findByRole('link', { name: /^BTCUSDT.*60\s000/ });
  const reads = () => requests.filter(({ url }) => url === '/bff/me/positions?symbol=BTCUSDT').length;
  const originalReads = reads();
  act(() => updates.toggle());
  price = 61000;
  await act(async () => emit({ type: 'recovery' }));
  await waitFor(() => expect(reads()).toBeGreaterThan(originalReads));
  expect(screen.getByRole('link', { name: /^BTCUSDT.*60\s000/ })).toBeInTheDocument();
  expect(screen.queryByRole('link', { name: /^BTCUSDT.*61\s000/ })).not.toBeInTheDocument();
  act(() => updates.toggle());
  await screen.findByRole('link', { name: /^BTCUSDT.*61\s000/ });
  expect(updates.getSnapshot().mode).toBe('live');
});

it.each(['страница', 'ошибка', 'смена символа', 'отмена ожидания'] as const)('сохраняет ручное чтение при возобновлении: %s', async (outcome) => {
  let resolveManual!: (response: Response) => void;
  const manual = new Promise<Response>((resolve) => { resolveManual = resolve; });
  let reads = 0;
  const page = (price: number, cursor: string | null, id = 'position-1') => jsonResponse({
    items: [positionFixture({ id, symbol: 'BTCUSDT', averageEntryPrice: price })],
    hasMore: cursor !== null,
    nextCursor: cursor,
  });
  const { requests, updates } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => ++reads === 1 ? page(60000, 'old') : page(62000, 'fresh'),
    '/bff/me/positions?symbol=BTCUSDT&cursor=old': () => manual,
    '/bff/me/positions?symbol=BTCUSDT&cursor=fresh': () => page(63000, null, 'position-2'),
    '/bff/me/positions?symbol=ETHUSDT': () => results('ETHUSDT'),
    '/bff/me/positions?trackingState=closed&symbol=ETHUSDT': empty,
  });
  await screen.findByRole('link', { name: /^BTCUSDT.*60\s000/ });
  act(() => updates.toggle());
  await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
  const request = requests.find(({ url }) => url.endsWith('cursor=old'));
  await act(async () => updates.toggle());
  expect(updates.getSnapshot().mode).toBe('resuming');
  expect(request?.init?.signal?.aborted).toBe(false);
  expect(reads).toBe(1);
  if (outcome === 'отмена ожидания') {
    await act(async () => updates.cancelResume());
    expect(updates.getSnapshot().mode).toBe('paused');
    expect(request?.init?.signal?.aborted).toBe(false);
    await act(async () => resolveManual(page(61000, null, 'position-2')));
    expect(await screen.findByRole('link', { name: /^BTCUSDT.*61\s000/ })).toBeVisible();
    expect(reads).toBe(1);
    return;
  }
  if (outcome === 'смена символа') {
    await userEvent.clear(screen.getByLabelText('Символ'));
    await userEvent.type(screen.getByLabelText('Символ'), 'ETHUSDT');
    await userEvent.click(screen.getByRole('button', { name: 'Найти' }));
    await screen.findByRole('link', { name: /^ETHUSDT/ });
    expect(request?.init?.signal?.aborted).toBe(true);
    await act(async () => resolveManual(page(61000, null, 'position-2')));
    expect(screen.queryByRole('link', { name: /^BTCUSDT/ })).not.toBeInTheDocument();
    return;
  }
  await act(async () => resolveManual(outcome === 'ошибка' ? jsonResponse({}, 503) : page(61000, null, 'position-2')));
  await screen.findByRole('link', { name: /^BTCUSDT.*62\s000/ });
  expect(updates.getSnapshot().mode).toBe('live');
  if (outcome === 'страница') {
    expect(screen.getByRole('link', { name: /^BTCUSDT.*63\s000/ })).toBeVisible();
  } else {
    expect(screen.getByRole('alert')).toHaveTextContent('Не удалось');
    expect(requests.some(({ url }) => url.endsWith('cursor=fresh'))).toBe(false);
  }
});

it.each([false, true])('сохраняет ручную страницу при recovery, начатом заранее: %s', async (startedEarlier) => {
  let firstReads = 0;
  let replyOld!: (response: Response) => void;
  let replyManual!: (response: Response) => void;
  let replyFresh!: (response: Response) => void;
  const old = new Promise<Response>((resolve) => { replyOld = resolve; });
  const manual = new Promise<Response>((resolve) => { replyManual = resolve; });
  const fresh = new Promise<Response>((resolve) => { replyFresh = resolve; });
  const page = (price: number, cursor: string | null, id = 'position-1') => jsonResponse({
    items: [positionFixture({ id, symbol: 'BTCUSDT', averageEntryPrice: price })],
    hasMore: cursor !== null,
    nextCursor: cursor,
  });
  const { requests, emit } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => {
      firstReads += 1;
      if (firstReads === 1) return page(60000, 'old');
      if (startedEarlier && firstReads === 2) return old;
      return page(62000, 'fresh');
    },
    '/bff/me/positions?symbol=BTCUSDT&cursor=old': () => manual,
    '/bff/me/positions?symbol=BTCUSDT&cursor=fresh': () => fresh,
  });
  const user = userEvent.setup();
  await screen.findByRole('link', { name: /^BTCUSDT.*60\s000/ });
  if (startedEarlier) {
    await act(async () => emit({ type: 'recovery' }));
    expect(firstReads).toBe(2);
  }

  await user.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
  const pending = requests.find(({ url }) => url.endsWith('cursor=old'));
  await act(async () => {
    emit({ type: 'recovery' });
    emit({ type: 'recovery' });
    if (startedEarlier) replyOld(page(59000, 'unused'));
  });
  expect(firstReads).toBe(startedEarlier ? 2 : 1);
  expect(pending?.init?.signal?.aborted).toBe(false);
  expect(screen.getByRole('link', { name: /^BTCUSDT.*60\s000/ })).toBeVisible();

  await act(async () => replyManual(page(61000, null, 'position-2')));
  await screen.findByRole('link', { name: /^BTCUSDT.*61\s000/ });
  await waitFor(() => expect(requests.filter(({ url }) => url.endsWith('cursor=fresh'))).toHaveLength(1));
  expect(firstReads).toBe(startedEarlier ? 3 : 2);
  await act(async () => replyFresh(page(63000, null, 'position-2')));
  await screen.findByRole('link', { name: /^BTCUSDT.*63\s000/ });
  expect(screen.getByRole('link', { name: /^BTCUSDT.*62\s000/ })).toBeVisible();
  expect(screen.queryByRole('link', { name: /^BTCUSDT.*61\s000/ })).not.toBeInTheDocument();
});

it('обрабатывает отказы REST на паузе и скрывает снимок при отзыве доступа', async () => {
  let status = 200;
  const endSession = vi.fn();
  const { updates, emit } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => status === 200 ? results('BTCUSDT') : jsonResponse(null, status),
  }, () => ({ isCurrent: () => true, endSession }));
  await screen.findByRole('link', { name: /^BTCUSDT.*60\s000/ });
  const checkAccess = vi.fn();
  updates.setAccessCheck(checkAccess);
  act(() => updates.toggle());
  for (const failure of [401, 403]) {
    status = failure;
    await act(async () => emit({ type: 'recovery' }));
  }
  expect(endSession).toHaveBeenCalledExactlyOnceWith(null);
  expect(checkAccess).toHaveBeenCalledExactlyOnceWith(403, null);
  expect(screen.queryByRole('link', { name: /^BTCUSDT.*60\s000/ })).not.toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

it('отменяет ручной continuation и отложенный recovery при смене символа', async () => {
  let replyManual!: (response: Response) => void;
  const manual = new Promise<Response>((resolve) => { replyManual = resolve; });
  const { emit, requests } = renderSearch({
    '/bff/me/positions?symbol=BTCUSDT': () => jsonResponse({
      items: [positionFixture({ symbol: 'BTCUSDT' })], hasMore: true, nextCursor: 'old',
    }),
    '/bff/me/positions?symbol=BTCUSDT&cursor=old': () => manual,
    '/bff/me/positions?symbol=ETHUSDT': () => results('ETHUSDT'),
    '/bff/me/positions?trackingState=closed&symbol=ETHUSDT': empty,
  });
  const user = userEvent.setup();
  await user.click(await screen.findByRole('button', { name: 'Загрузить ещё' }));
  await act(async () => emit({ type: 'recovery' }));
  const symbol = screen.getByLabelText('Символ');
  await user.clear(symbol);
  await user.type(symbol, 'ETHUSDT');
  await user.click(screen.getByRole('button', { name: 'Найти' }));
  await screen.findByRole('link', { name: /^ETHUSDT/ });
  expect(requests.find(({ url }) => url.endsWith('cursor=old'))?.init?.signal?.aborted).toBe(true);
  await act(async () => replyManual(results('BTCUSDT', 64000)));
  expect(requests.filter(({ url }) => url === '/bff/me/positions?symbol=BTCUSDT')).toHaveLength(1);
  expect(screen.queryByRole('link', { name: /^BTCUSDT/ })).not.toBeInTheDocument();
});
