import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../auth/AuthProvider';
import { AppRoutes } from '../app/router';
import type { ExchangeAccount } from '../connections/connectionTypes';
import type { Portfolio, PositionListItem } from '../portfolio/portfolioTypes';
import { jsonResponse, mockFetch, type RecordedRequest, type RouteHandler } from '../test/fetchMock';

const ACCOUNTS_URL = '/bff/me/exchange-accounts';
const MAIN_ID = '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6';
const SECOND_ID = '3a7e5b1c-2d4f-4e6a-9b8c-0d1e2f3a4b5c';
const UNAVAILABLE_ID = '4b8f6c2d-3e5a-4f7b-8c9d-1e2f3a4b5c6d';
const DISABLED_ID = '6b1d2c3e-4f5a-4b6c-8d7e-9f0a1b2c3d4e';
const UNKNOWN_ID = '7c2e3d4f-5a6b-4c7d-9e8f-2a3b4c5d6e7f';

const authenticatedSession = {
  authenticated: true,
  user: { userId: '7a0c8b5e-6c1d-4f3e-9a6b-2d4e8f1a3c5b', subject: '7a0c8b5e-6c1d-4f3e-9a6b-2d4e8f1a3c5b' },
};

function account(id: string, displayName: string, connectionStatus: ExchangeAccount['connectionStatus']): ExchangeAccount {
  return {
    id,
    displayName,
    exchange: 'bybit',
    connectionStatus,
    capabilities: connectionStatus === 'disabled' ? [] : ['readBalance', 'readPositions'],
    lastSyncedAt: connectionStatus === 'unknown' ? null : '2026-10-07T09:58:00Z',
  };
}

const mainAccount = account(MAIN_ID, 'Основной', 'connected');
const secondAccount = account(SECOND_ID, 'Второй', 'connected');
const unavailableAccount = account(UNAVAILABLE_ID, 'Недоступный', 'unavailable');
const disabledAccount = account(DISABLED_ID, 'Отключённый', 'disabled');
const unknownAccount = account(UNKNOWN_ID, 'Новый', 'unknown');

function portfolio(overrides: Partial<Portfolio> = {}, accountId = MAIN_ID): Portfolio {
  return {
    exchangeAccountId: accountId,
    calculatedAt: '2026-10-07T09:58:00Z',
    capital: { totalEquity: 18420, availableCapital: 7310, totalWalletBalance: 18155, observedAt: '2026-10-07T09:58:00Z' },
    totalUnrealizedPnl: 265,
    positionsFullyReconciled: true,
    isComplete: true,
    isFresh: true,
    currentPositionCount: 3,
    exposures: [{ settlementAsset: 'USDT', grossExposure: 13000, longExposure: 10000, shortExposure: 3000 }],
    ...overrides,
  };
}

function position(overrides: Partial<PositionListItem> = {}): PositionListItem {
  return {
    id: '8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001',
    exchangeAccountId: MAIN_ID,
    symbol: 'BTCUSDT',
    side: 'long',
    trackingState: 'active',
    size: 0.15,
    averageEntryPrice: 68500,
    markPrice: 70120,
    positionValue: 10000,
    unrealizedPnl: 230,
    leverage: 5,
    liquidationPrice: null,
    firstDetectedAt: '2026-10-01T10:00:00Z',
    lastObservedAt: '2026-10-07T09:58:00Z',
    closedAt: null,
    settlementAsset: 'USDT',
    ...overrides,
  };
}

const ethPosition = position({
  id: '8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c002',
  symbol: 'ETHUSDC',
  size: 2.4,
  positionValue: 5000,
  unrealizedPnl: 35,
  settlementAsset: 'USDC',
});

const portfolioUrl = (id: string) => `${ACCOUNTS_URL}/${id}/portfolio`;
const syncUrl = (id: string) => `${ACCOUNTS_URL}/${id}/sync`;
const positionsUrl = (id: string, query = '') => `/bff/me/positions?exchangeAccountId=${id}${query}`;

function page(items: PositionListItem[], nextCursor: string | null = null): Response {
  return jsonResponse({ items, nextCursor, hasMore: nextCursor !== null });
}

function problem(status: number, code: string, traceId = '00-trace-01'): Response {
  return new Response(
    JSON.stringify({ title: 'Server title must not be shown', detail: 'Server detail', status, code, traceId }),
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );
}

function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((settle) => {
    resolve = settle;
  });
  return { promise, resolve };
}

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.search}</output>;
}

function renderHome(path: string, routes: Record<string, RouteHandler>): RecordedRequest[] {
  const requests = mockFetch({
    '/bff/auth/session': () => jsonResponse(authenticatedSession),
    '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
    ...routes,
  });
  render(
    <AuthProvider>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes />
        <LocationProbe />
      </MemoryRouter>
    </AuthProvider>,
  );
  return requests;
}

/** Стандартный набор ответов для одного выбранного подключения. */
function accountRoutes(
  accounts: ExchangeAccount[],
  selectedId = MAIN_ID,
  options: { portfolio?: RouteHandler; positions?: RouteHandler } = {},
): Record<string, RouteHandler> {
  return {
    [ACCOUNTS_URL]: () => jsonResponse({ items: accounts }),
    [portfolioUrl(selectedId)]: options.portfolio ?? (() => jsonResponse(portfolio({}, selectedId))),
    [positionsUrl(selectedId)]: options.positions ?? (() => page([position()])),
  };
}

function searchParams(): URLSearchParams {
  return new URLSearchParams(screen.getByTestId('location').textContent ?? '');
}

const plain = (value: string | null | undefined) => (value ?? '').replace(/\s+/g, ' ').trim();

function valueOf(scope: HTMLElement, label: string): string {
  return plain(within(scope).getByText(label, { selector: 'dt' }).nextElementSibling?.textContent);
}

function urls(requests: RecordedRequest[], url: string): RecordedRequest[] {
  return requests.filter((request) => request.url === url);
}

const findSummary = () => screen.findByRole('region', { name: 'Сводка' });

describe('HomePage account selection', () => {
  it('shows an empty state with a link to connections when there are no accounts', async () => {
    const requests = renderHome('/app', { [ACCOUNTS_URL]: () => jsonResponse({ items: [] }) });

    const link = await screen.findByRole('link', { name: 'Подключить биржу' });
    expect(link).toHaveAttribute('href', '/app/settings/connections');
    expect(requests.some((request) => request.url.includes('/portfolio') || request.url.includes('/positions'))).toBe(false);
  });

  it('selects the only account automatically and writes it to the URL', async () => {
    renderHome(`/app?account=${SECOND_ID}`, accountRoutes([mainAccount]));

    await findSummary();
    expect(screen.getByRole('heading', { name: 'Основной' })).toBeVisible();
    expect(screen.queryByLabelText('Подключение')).not.toBeInTheDocument();
    await waitFor(() => expect(searchParams().get('account')).toBe(MAIN_ID));
  });

  it('selects the first connected account among many', async () => {
    const requests = renderHome(
      '/app',
      accountRoutes([unknownAccount, disabledAccount, secondAccount, mainAccount], SECOND_ID),
    );

    await findSummary();
    expect(screen.getByLabelText('Подключение')).toHaveValue(SECOND_ID);
    expect(within(screen.getByLabelText('Подключение')).getAllByRole('option').map((option) => plain(option.textContent))).toEqual([
      'Новый — Не проверено',
      'Отключённый — Отключено',
      'Второй — Подключено',
      'Основной — Подключено',
    ]);
    await waitFor(() => expect(searchParams().get('account')).toBe(SECOND_ID));
    expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(0);
  });

  it('selects the account from the URL when it belongs to the user', async () => {
    renderHome(`/app?account=${MAIN_ID}`, accountRoutes([secondAccount, mainAccount], MAIN_ID));

    await findSummary();
    expect(screen.getByLabelText('Подключение')).toHaveValue(MAIN_ID);
  });

  it('does not request data for a foreign URL account and falls back to the first connected', async () => {
    const foreignId = '9f9f9f9f-0000-4000-8000-000000000000';
    const requests = renderHome(`/app?account=${foreignId}`, accountRoutes([secondAccount, mainAccount], SECOND_ID));

    await findSummary();
    expect(screen.getByLabelText('Подключение')).toHaveValue(SECOND_ID);
    expect(requests.some((request) => request.url.includes(foreignId))).toBe(false);
    await waitFor(() => expect(searchParams().get('account')).toBe(SECOND_ID));
  });

  it('keeps the saved portfolio of an unavailable account visible and allows sync', async () => {
    renderHome(`/app?account=${UNAVAILABLE_ID}`, accountRoutes([mainAccount, unavailableAccount], UNAVAILABLE_ID));

    await findSummary();
    expect(screen.getByText('Подключение недоступно: показаны последние сохранённые данные.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
  });

  it('disables sync for a disabled account', async () => {
    renderHome(`/app?account=${DISABLED_ID}`, accountRoutes([mainAccount, disabledAccount], DISABLED_ID));

    await findSummary();
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeDisabled();
    expect(screen.getByText('Подключение отключено: синхронизация недоступна.')).toBeVisible();
  });

  it('selects nothing automatically when no account is connected and loads data after a choice', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([unknownAccount, disabledAccount], UNKNOWN_ID),
    });

    const selector = await screen.findByLabelText('Подключение');
    expect(selector).toHaveValue('');
    expect(screen.getByText(/Нет активных подключений/)).toBeVisible();
    expect(requests.some((request) => request.url.includes('/portfolio') || request.url.includes('/positions'))).toBe(false);

    await userEvent.selectOptions(selector, UNKNOWN_ID);

    await findSummary();
    expect(searchParams().get('account')).toBe(UNKNOWN_ID);
    expect(urls(requests, portfolioUrl(UNKNOWN_ID))).toHaveLength(1);
  });
});

describe('HomePage portfolio summary', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows a loading state while the portfolio is requested', async () => {
    renderHome('/app', accountRoutes([mainAccount], MAIN_ID, { portfolio: () => new Promise<Response>(() => {}) }));

    expect(await screen.findByText('Загружаем портфель…')).toBeVisible();
  });

  it('explains a missing portfolio for 204', async () => {
    renderHome(
      '/app',
      accountRoutes([mainAccount], MAIN_ID, { portfolio: () => new Response(null, { status: 204 }) }),
    );

    expect(await screen.findByText('Данных портфеля пока нет. Запустите синхронизацию подключения.')).toBeVisible();
    expect(screen.queryByRole('region', { name: 'Сводка' })).not.toBeInTheDocument();
  });

  it('shows account-level values in USD, the position count and fresh complete data', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-07T10:00:00Z'));
    renderHome('/app', accountRoutes([mainAccount]));

    const summary = await findSummary();
    expect(valueOf(summary, 'Общий капитал')).toBe('$18 420');
    expect(valueOf(summary, 'Доступно')).toBe('$7 310');
    expect(valueOf(summary, 'Нереализованный PnL')).toBe('+$265');
    expect(valueOf(summary, 'Открытые позиции')).toBe('3');
    const account = screen.getByRole('region', { name: 'Основной' });
    expect(within(account).getByText('Подключено')).toBeVisible();
    expect(within(account).getByText('Данные актуальны')).toBeVisible();
    expect(valueOf(account, 'Обновлено')).toBe('2 минуты назад');
    expect(within(account).queryByText('Данные неполные')).not.toBeInTheDocument();
  });

  it('marks stale data with the last successful sync', async () => {
    renderHome('/app', accountRoutes([mainAccount], MAIN_ID, { portfolio: () => jsonResponse(portfolio({ isFresh: false })) }));

    await findSummary();
    const account = screen.getByRole('region', { name: 'Основной' });
    expect(within(account).getByText(/Данные устарели/)).toHaveTextContent('Последняя успешная синхронизация');
    expect(within(account).queryByText('Данные актуальны')).not.toBeInTheDocument();
  });

  it('marks partial data', async () => {
    renderHome('/app', accountRoutes([mainAccount], MAIN_ID, { portfolio: () => jsonResponse(portfolio({ isComplete: false })) }));

    await findSummary();
    expect(screen.getByText('Данные неполные')).toBeVisible();
  });

  it('shows a portfolio error by machine-readable status without server text', async () => {
    renderHome(
      '/app',
      accountRoutes([mainAccount], MAIN_ID, { portfolio: () => problem(503, 'service_unavailable', '00-trace-07') }),
    );

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Сервис временно недоступен. Повторите попытку позже.');
    expect(alert).toHaveTextContent('00-trace-07');
    expect(alert).not.toHaveTextContent('Server title must not be shown');
  });

  it('shows a single USDT exposure group', async () => {
    renderHome('/app', accountRoutes([mainAccount]));

    const block = await screen.findByRole('region', { name: 'Экспозиция — USDT' });
    const group = within(block).getByRole('region', { name: 'Экспозиция USDT' });
    expect(valueOf(group, 'Всего')).toBe('13 000 USDT');
    expect(valueOf(group, 'Long')).toBe('10 000 USDT');
    expect(valueOf(group, 'Short')).toBe('3 000 USDT');
  });

  it('shows a single USDC exposure group', async () => {
    renderHome(
      '/app',
      accountRoutes([mainAccount], MAIN_ID, {
        portfolio: () =>
          jsonResponse(
            portfolio({ exposures: [{ settlementAsset: 'USDC', grossExposure: 5000, longExposure: 5000, shortExposure: 0 }] }),
          ),
      }),
    );

    const block = await screen.findByRole('region', { name: 'Экспозиция — USDC' });
    expect(valueOf(block, 'Всего')).toBe('5 000 USDC');
    expect(valueOf(block, 'Short')).toBe('0 USDC');
  });

  it('shows USDT and USDC exposure separately without a cross-asset total', async () => {
    renderHome(
      '/app',
      accountRoutes([mainAccount], MAIN_ID, {
        portfolio: () =>
          jsonResponse(
            portfolio({
              exposures: [
                { settlementAsset: 'USDC', grossExposure: 5000, longExposure: 5000, shortExposure: 0 },
                { settlementAsset: 'USDT', grossExposure: 13000, longExposure: 10000, shortExposure: 3000 },
              ],
            }),
          ),
      }),
    );

    const block = await screen.findByRole('region', { name: 'Экспозиция' });
    expect(valueOf(within(block).getByRole('region', { name: 'Экспозиция USDC' }), 'Всего')).toBe('5 000 USDC');
    expect(valueOf(within(block).getByRole('region', { name: 'Экспозиция USDT' }), 'Всего')).toBe('13 000 USDT');
    expect(plain(block.textContent)).not.toContain('18 000');
  });
});

describe('HomePage positions', () => {
  it('shows current positions with values in their settlement asset', async () => {
    renderHome('/app', accountRoutes([mainAccount], MAIN_ID, { positions: () => page([position(), ethPosition]) }));

    const btc = await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' });
    expect(valueOf(btc, 'Вход')).toBe('68 500');
    expect(valueOf(btc, 'Mark')).toBe('70 120');
    expect(valueOf(btc, 'Стоимость')).toBe('10 000 USDT');
    expect(valueOf(btc, 'PnL')).toBe('+230 USDT');
    expect(valueOf(btc, 'Плечо')).toBe('5x');
    const eth = screen.getByRole('article', { name: 'ETHUSDC · 2,4 LONG' });
    expect(valueOf(eth, 'Стоимость')).toBe('5 000 USDC');
    expect(valueOf(eth, 'PnL')).toBe('+35 USDC');
    expect(screen.getByRole('button', { name: 'Текущие' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('switches to closed positions, always sends trackingState=closed and drops the state filter', async () => {
    const closed = position({ trackingState: 'closed', closedAt: '2026-10-06T12:00:00Z', unrealizedPnl: null });
    const requests = renderHome(`/app?account=${MAIN_ID}&state=stale`, {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID, '&trackingState=stale')]: () => page([]),
      [positionsUrl(MAIN_ID, '&trackingState=closed')]: () => page([closed]),
    });
    await screen.findByText('Текущих позиций нет.');

    await userEvent.click(screen.getByRole('button', { name: 'Закрытые' }));

    const card = await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' });
    expect(within(card).getByText('Закрыта', { selector: 'span' })).toBeVisible();
    expect(urls(requests, positionsUrl(MAIN_ID, '&trackingState=closed'))).toHaveLength(1);
    expect(searchParams().get('view')).toBe('closed');
    expect(searchParams().has('state')).toBe(false);
    expect(screen.queryByLabelText('Состояние')).not.toBeInTheDocument();
  });

  it('filters current positions by tracking state', async () => {
    const stale = position({ trackingState: 'stale' });
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID, '&trackingState=stale')]: () => page([stale]),
    });
    await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.selectOptions(screen.getByLabelText('Состояние'), 'stale');

    await screen.findByText('Устарела');
    expect(urls(requests, positionsUrl(MAIN_ID, '&trackingState=stale'))).toHaveLength(1);
    expect(searchParams().get('state')).toBe('stale');
  });

  it('filters positions by side', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID, '&side=short')]: () => page([position({ side: 'short' })]),
    });
    await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.selectOptions(screen.getByLabelText('Направление'), 'short');

    expect(await screen.findByRole('article', { name: 'BTCUSDT · 0,15 SHORT' })).toBeVisible();
    expect(urls(requests, positionsUrl(MAIN_ID, '&side=short'))).toHaveLength(1);
    expect(searchParams().get('side')).toBe('short');
  });

  it('filters positions by exact symbol through the backend', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => page([position(), ethPosition]) }),
      [positionsUrl(MAIN_ID, '&symbol=ETHUSDC')]: () => page([ethPosition]),
    });
    await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.type(screen.getByLabelText('Символ'), '  ETHUSDC ');
    await userEvent.click(screen.getByRole('button', { name: 'Найти' }));

    await waitFor(() =>
      expect(screen.queryByRole('article', { name: 'BTCUSDT · 0,15 LONG' })).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('article', { name: 'ETHUSDC · 2,4 LONG' })).toBeVisible();
    expect(urls(requests, positionsUrl(MAIN_ID, '&symbol=ETHUSDC'))).toHaveLength(1);
    expect(searchParams().get('symbol')).toBe('ETHUSDC');
  });

  it('appends the next page with the opaque cursor', async () => {
    const second = position({ id: 'second', symbol: 'SOLUSDT', size: 10 });
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => page([position()], 'opaque+cursor/=') }),
      [positionsUrl(MAIN_ID, '&cursor=opaque%2Bcursor%2F%3D')]: () => page([second]),
    });
    await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));

    expect(await screen.findByRole('article', { name: 'SOLUSDT · 10 LONG' })).toBeVisible();
    expect(screen.getByRole('article', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
    expect(urls(requests, positionsUrl(MAIN_ID, '&cursor=opaque%2Bcursor%2F%3D'))).toHaveLength(1);
    expect(searchParams().has('cursor')).toBe(false);
  });
});

describe('HomePage manual sync', () => {
  it('syncs with antiforgery and reloads connections, portfolio and the first positions page', async () => {
    const sync = deferred<Response>();
    let portfolioCalls = 0;
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, {
        portfolio: () => {
          portfolioCalls += 1;
          return jsonResponse(portfolio({ currentPositionCount: portfolioCalls === 1 ? 3 : 4 }));
        },
      }),
      [syncUrl(MAIN_ID)]: () => sync.promise,
    });
    await findSummary();

    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));

    expect(await screen.findByRole('button', { name: 'Синхронизация…' })).toBeDisabled();
    sync.resolve(jsonResponse(mainAccount));
    expect(await screen.findByText('Синхронизация завершена.')).toBeVisible();
    await waitFor(() => expect(valueOf(screen.getByRole('region', { name: 'Сводка' }), 'Открытые позиции')).toBe('4'));
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    const post = urls(requests, syncUrl(MAIN_ID));
    expect(post).toHaveLength(1);
    expect(post[0]?.init?.method).toBe('POST');
    expect(new Headers(post[0]?.init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
    expect(urls(requests, ACCOUNTS_URL)).toHaveLength(2);
    expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(2);
    expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(2);
    expect(requests.some((request) => request.url.includes('evaluation'))).toBe(false);
  });

  it('keeps the last successful portfolio and positions visible when the post-sync reload fails', async () => {
    let portfolioCalls = 0;
    let positionsCalls = 0;
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, {
        portfolio: () => {
          portfolioCalls += 1;
          return portfolioCalls === 1 ? jsonResponse(portfolio()) : problem(503, 'service_unavailable');
        },
        positions: () => {
          positionsCalls += 1;
          return positionsCalls === 1 ? page([position()], 'opaque+cursor/=') : problem(503, 'service_unavailable');
        },
      }),
      [syncUrl(MAIN_ID)]: () => jsonResponse(mainAccount),
    });

    const summary = await findSummary();
    expect(valueOf(summary, 'Открытые позиции')).toBe('3');
    expect(await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Загрузить ещё' })).toBeVisible();

    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));

    expect(await screen.findByText('Синхронизация завершена.')).toBeVisible();
    await waitFor(() => {
      expect(portfolioCalls).toBe(2);
      expect(positionsCalls).toBe(2);
    });

    expect(valueOf(screen.getByRole('region', { name: 'Сводка' }), 'Открытые позиции')).toBe('3');
    expect(screen.getByRole('article', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
    expect(screen.getAllByText('Сервис временно недоступен. Повторите попытку позже.')).toHaveLength(2);
    expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(2);
    expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(2);
  });

  it('shows a sync failure without retry and still reloads factual state', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [syncUrl(MAIN_ID)]: () => problem(503, 'exchange_unavailable'),
    });
    await findSummary();

    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Bybit сейчас недоступен. Повторите попытку позже.');
    await waitFor(() => expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(2));
    expect(urls(requests, syncUrl(MAIN_ID))).toHaveLength(1);
    expect(urls(requests, ACCOUNTS_URL)).toHaveLength(2);
    expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(2);
  });

  it('does not reload data when sync reports an ended session', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [syncUrl(MAIN_ID)]: () => new Response(null, { status: 401 }),
    });
    await findSummary();

    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Сессия завершена. Войдите снова.');
    expect(within(alert).getByRole('button', { name: 'Войти снова' })).toBeVisible();
    expect(urls(requests, ACCOUNTS_URL)).toHaveLength(1);
    expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(1);
  });

  it('ignores a repeated click while sync is running', async () => {
    const sync = deferred<Response>();
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [syncUrl(MAIN_ID)]: () => sync.promise,
    });
    await findSummary();

    await userEvent.dblClick(screen.getByRole('button', { name: 'Синхронизировать' }));
    await screen.findByRole('button', { name: 'Синхронизация…' });
    sync.resolve(jsonResponse(mainAccount));

    await screen.findByText('Синхронизация завершена.');
    expect(urls(requests, syncUrl(MAIN_ID))).toHaveLength(1);
    expect(urls(requests, '/bff/auth/antiforgery')).toHaveLength(1);
  });
});

describe('HomePage manual sync bound to its account', () => {
  function twoAccountRoutes(syncMain: RouteHandler): Record<string, RouteHandler> {
    return {
      [ACCOUNTS_URL]: () => jsonResponse({ items: [mainAccount, secondAccount] }),
      [portfolioUrl(MAIN_ID)]: () => jsonResponse(portfolio()),
      [portfolioUrl(SECOND_ID)]: () => jsonResponse(portfolio({ currentPositionCount: 7 }, SECOND_ID)),
      [positionsUrl(MAIN_ID)]: () => page([position()]),
      [positionsUrl(SECOND_ID)]: () => page([ethPosition]),
      [syncUrl(MAIN_ID)]: syncMain,
    };
  }

  async function startMainSyncAndSwitchToSecond(): Promise<void> {
    await findSummary();
    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));
    await screen.findByRole('button', { name: 'Синхронизация…' });

    await userEvent.selectOptions(screen.getByLabelText('Подключение'), SECOND_ID);
    await screen.findByRole('article', { name: 'ETHUSDC · 2,4 LONG' });
  }

  it('does not show the result of a sync started for another account and does not overwrite its data', async () => {
    const sync = deferred<Response>();
    const requests = renderHome(`/app?account=${MAIN_ID}`, twoAccountRoutes(() => sync.promise));
    await startMainSyncAndSwitchToSecond();

    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Синхронизация…' })).not.toBeInTheDocument();

    sync.resolve(jsonResponse(mainAccount));
    await waitFor(() => expect(urls(requests, ACCOUNTS_URL)).toHaveLength(2));
    await new Promise((settle) => setTimeout(settle, 0));

    expect(searchParams().get('account')).toBe(SECOND_ID);
    expect(screen.getByRole('heading', { name: 'Второй' })).toBeVisible();
    expect(valueOf(screen.getByRole('region', { name: 'Сводка' }), 'Открытые позиции')).toBe('7');
    expect(screen.getByRole('article', { name: 'ETHUSDC · 2,4 LONG' })).toBeVisible();
    expect(screen.queryByRole('article', { name: 'BTCUSDT · 0,15 LONG' })).not.toBeInTheDocument();
    expect(screen.queryByText('Синхронизация завершена.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(1);
    expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(1);
    expect(urls(requests, portfolioUrl(SECOND_ID))).toHaveLength(1);
    expect(urls(requests, positionsUrl(SECOND_ID))).toHaveLength(1);

    await userEvent.selectOptions(screen.getByLabelText('Подключение'), MAIN_ID);
    expect(await screen.findByText('Синхронизация завершена.')).toBeVisible();
    expect(screen.getByRole('heading', { name: 'Основной' })).toBeVisible();
  });

  it('does not start a second sync of the same account while the first one is running', async () => {
    const sync = deferred<Response>();
    const requests = renderHome(`/app?account=${MAIN_ID}`, twoAccountRoutes(() => sync.promise));
    await startMainSyncAndSwitchToSecond();

    await userEvent.selectOptions(screen.getByLabelText('Подключение'), MAIN_ID);
    const running = await screen.findByRole('button', { name: 'Синхронизация…' });
    expect(running).toBeDisabled();
    await userEvent.dblClick(running);

    sync.resolve(jsonResponse(mainAccount));
    expect(await screen.findByText('Синхронизация завершена.')).toBeVisible();
    expect(urls(requests, syncUrl(MAIN_ID))).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
  });

  it('does not show a sync error of another account under the selected one', async () => {
    const sync = deferred<Response>();
    const requests = renderHome(`/app?account=${MAIN_ID}`, twoAccountRoutes(() => sync.promise));
    await startMainSyncAndSwitchToSecond();

    sync.resolve(problem(503, 'exchange_unavailable'));
    await waitFor(() => expect(urls(requests, ACCOUNTS_URL)).toHaveLength(2));
    await new Promise((settle) => setTimeout(settle, 0));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByText('Bybit сейчас недоступен. Повторите попытку позже.')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Второй' })).toBeVisible();
    expect(screen.getByRole('article', { name: 'ETHUSDC · 2,4 LONG' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    expect(urls(requests, syncUrl(MAIN_ID))).toHaveLength(1);
  });
});

describe('HomePage async race protection', () => {
  it('does not let a late portfolio of the previous account overwrite the selected one', async () => {
    const lateMain = deferred<Response>();
    renderHome(`/app?account=${MAIN_ID}`, {
      [ACCOUNTS_URL]: () => jsonResponse({ items: [mainAccount, secondAccount] }),
      [portfolioUrl(MAIN_ID)]: () => lateMain.promise,
      [portfolioUrl(SECOND_ID)]: () => jsonResponse(portfolio({ currentPositionCount: 7 }, SECOND_ID)),
      [positionsUrl(MAIN_ID)]: () => page([position()]),
      [positionsUrl(SECOND_ID)]: () => page([ethPosition]),
    });
    await screen.findByText('Загружаем портфель…');

    await userEvent.selectOptions(screen.getByLabelText('Подключение'), SECOND_ID);
    const summary = await findSummary();
    lateMain.resolve(jsonResponse(portfolio({ currentPositionCount: 1 })));

    await screen.findByRole('article', { name: 'ETHUSDC · 2,4 LONG' });
    await new Promise((settle) => setTimeout(settle, 0));
    expect(valueOf(summary, 'Открытые позиции')).toBe('7');
    expect(screen.getByRole('heading', { name: 'Второй' })).toBeVisible();
  });

  it('does not let a late first page of the previous filter overwrite the current list', async () => {
    const lateAll = deferred<Response>();
    renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => lateAll.promise }),
      [positionsUrl(MAIN_ID, '&side=short')]: () => page([position({ side: 'short' })]),
    });
    await findSummary();

    await userEvent.selectOptions(screen.getByLabelText('Направление'), 'short');
    await screen.findByRole('article', { name: 'BTCUSDT · 0,15 SHORT' });
    lateAll.resolve(page([ethPosition]));

    await new Promise((settle) => setTimeout(settle, 0));
    expect(screen.queryByRole('article', { name: 'ETHUSDC · 2,4 LONG' })).not.toBeInTheDocument();
    expect(screen.getByRole('article', { name: 'BTCUSDT · 0,15 SHORT' })).toBeVisible();
  });

  it('does not continue the list with a cursor obtained before the reload after sync', async () => {
    const reload = deferred<Response>();
    let firstPageCalls = 0;
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, {
        positions: () => {
          firstPageCalls += 1;
          return firstPageCalls === 1 ? page([position()], 'next') : reload.promise;
        },
      }),
      [positionsUrl(MAIN_ID, '&cursor=next')]: () => page([ethPosition], 'stale'),
      [syncUrl(MAIN_ID)]: () => jsonResponse(mainAccount),
    });
    await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' });
    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
    await screen.findByRole('article', { name: 'ETHUSDC · 2,4 LONG' });

    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));
    await waitFor(() => expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(2));

    expect(screen.getByRole('article', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();

    reload.resolve(page([position({ id: 'fresh', symbol: 'SOLUSDT', side: 'short' })], 'fresh-next'));
    await screen.findByRole('article', { name: 'SOLUSDT · 0,15 SHORT' });
    expect(screen.getAllByRole('article')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Загрузить ещё' })).toBeEnabled();
    expect(urls(requests, positionsUrl(MAIN_ID, '&cursor=stale'))).toHaveLength(0);
  });

  it('does not append a late next page after the account changes', async () => {
    const lateMore = deferred<Response>();
    renderHome(`/app?account=${MAIN_ID}`, {
      [ACCOUNTS_URL]: () => jsonResponse({ items: [mainAccount, secondAccount] }),
      [portfolioUrl(MAIN_ID)]: () => jsonResponse(portfolio()),
      [portfolioUrl(SECOND_ID)]: () => jsonResponse(portfolio({}, SECOND_ID)),
      [positionsUrl(MAIN_ID)]: () => page([position()], 'next'),
      [positionsUrl(MAIN_ID, '&cursor=next')]: () => lateMore.promise,
      [positionsUrl(SECOND_ID)]: () => page([ethPosition]),
    });
    await screen.findByRole('article', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
    await userEvent.selectOptions(screen.getByLabelText('Подключение'), SECOND_ID);
    await screen.findByRole('article', { name: 'ETHUSDC · 2,4 LONG' });
    lateMore.resolve(page([position({ id: 'late', symbol: 'LATEUSDT' })]));

    await new Promise((settle) => setTimeout(settle, 0));
    expect(screen.queryByText(/LATEUSDT/)).not.toBeInTheDocument();
    expect(screen.getAllByRole('article')).toHaveLength(1);
  });
});
