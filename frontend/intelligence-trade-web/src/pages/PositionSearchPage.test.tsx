import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { createAutoUpdates } from '../realtime/autoUpdates';
import { AutoUpdatesContext } from '../realtime/autoUpdatesContext';
import { RealtimeContext } from '../realtime/realtimeContext';
import type { RealtimeInvalidation, RealtimeListener } from '../realtime/realtimeTypes';
import { jsonResponse, mockFetch, type RouteHandler } from '../test/fetchMock';
import { positionFixture } from '../test/positionFixtures';
import { PositionSearchPage } from './PositionSearchPage';

const empty = () => jsonResponse({ items: [], hasMore: false, nextCursor: null });
const results = (symbol: string, averageEntryPrice = 60000) => jsonResponse({
  items: [positionFixture({ symbol, averageEntryPrice })], hasMore: false, nextCursor: null,
});

function renderSearch(routes: Record<string, RouteHandler>) {
  const requests = mockFetch({
    '/bff/me/exchange-accounts': () => jsonResponse({ items: [] }),
    '/bff/me/positions?trackingState=closed&symbol=BTCUSDT': empty,
    ...routes,
  });
  const updates = createAutoUpdates();
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
          <PositionSearchPage />
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
