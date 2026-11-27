import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../auth/AuthProvider';
import { AppRoutes } from '../app/router';
import type { ExchangeAccount } from '../connections/connectionTypes';
import type { Portfolio, PositionListItem } from '../portfolio/portfolioTypes';
import { jsonResponse, mockFetch, type RecordedRequest, type RouteHandler } from '../test/fetchMock';
import { candlesFixture, evaluationFixture, marketFixture, positionFixture } from '../test/positionFixtures';
import { SessionRecheck } from '../test/SessionRecheck';
import { emitRealtime } from '../test/realtimeTestDriver';

// jsdom не реализует canvas, который нужен графику страницы позиции.
vi.mock('lightweight-charts', () => ({
  CandlestickSeries: {},
  ColorType: { Solid: 'solid' },
  LineStyle: { Solid: 0, Dashed: 2 },
  createChart: () => ({
    addSeries: () => ({ setData: () => {}, applyOptions: () => {}, createPriceLine: () => ({}), removePriceLine: () => {} }),
    timeScale: () => ({ fitContent: () => {}, getVisibleRange: () => null }),
    priceScale: () => ({ setAutoScale: () => {}, options: () => ({ autoScale: true }), getVisibleRange: () => null }),
    remove: () => {},
  }),
}));

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
        <SessionRecheck userId={authenticatedSession.user.userId} />
      </MemoryRouter>
    </AuthProvider>,
    { wrapper: MantineProvider },
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

async function openAccountDetails(): Promise<HTMLDetailsElement> {
  const details = await waitFor(() => {
    const panel = document.querySelector<HTMLDetailsElement>('.overview-account');
    if (!panel) throw new Error('Сведения о подключении ещё не загружены.');
    return panel;
  });
  const summary = details.querySelector('summary');
  if (!details.open && summary) await userEvent.click(summary);
  return details;
}

describe('HomePage account selection', () => {
  it('shows an empty state with a link to connections when there are no accounts', async () => {
    const requests = renderHome('/app', { [ACCOUNTS_URL]: () => jsonResponse({ items: [] }) });

    const link = await screen.findByRole('link', { name: 'Подключить биржу' });
    expect(link).toHaveAttribute('href', '/app/settings/connections');
    expect(requests.some((request) => request.url.includes('/portfolio') || request.url.includes('/positions'))).toBe(false);
  });

  it('автоматически выбирает единственное подключение и записывает его в URL', async () => {
    renderHome(`/app?account=${SECOND_ID}`, accountRoutes([mainAccount]));

    await findSummary();
    expect(screen.getByText('Основной', { selector: 'summary' })).toBeVisible();
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

  it('показывает сохранённый портфель недоступного подключения и разрешает синхронизацию', async () => {
    renderHome(`/app?account=${UNAVAILABLE_ID}`, accountRoutes([mainAccount, unavailableAccount], UNAVAILABLE_ID));

    await findSummary();
    await openAccountDetails();
    expect(screen.getByText('Подключение недоступно: показаны последние сохранённые данные.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
  });

  it('отключает синхронизацию для отключённого подключения', async () => {
    renderHome(`/app?account=${DISABLED_ID}`, accountRoutes([mainAccount, disabledAccount], DISABLED_ID));

    await findSummary();
    await openAccountDetails();
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

  it('показывает суммы счёта в USD, количество позиций и свежесть полного снимка', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-10-07T10:00:00Z'));
    renderHome('/app', accountRoutes([mainAccount]));

    const summary = await findSummary();
    expect(valueOf(summary, 'Общий капитал')).toBe('$18 420');
    expect(valueOf(summary, 'Доступно')).toBe('$7 310');
    expect(valueOf(summary, 'Нереализованный PnL')).toBe('+$265');
    expect(screen.getByRole('button', { name: 'Открытые 3' })).toBeVisible();
    const account = await openAccountDetails();
    expect(within(account).getByText('Подключено')).toBeVisible();
    expect(within(account).getByText('REST-снимок актуален на момент ответа')).toBeVisible();
    expect(valueOf(account, 'Обновлено')).toBe('2 минуты назад');
    expect(within(account).queryByText('Данные неполные')).not.toBeInTheDocument();
  });

  it('отмечает устаревший снимок и время последней успешной синхронизации', async () => {
    renderHome('/app', accountRoutes([mainAccount], MAIN_ID, { portfolio: () => jsonResponse(portfolio({ isFresh: false })) }));

    await findSummary();
    const account = await openAccountDetails();
    expect(within(account).getByText(/REST-снимок устарел.*Последняя успешная синхронизация/)).toHaveTextContent('Последняя успешная синхронизация');
    expect(within(account).queryByText('REST-снимок актуален на момент ответа')).not.toBeInTheDocument();
  });

  it('отмечает неполные данные', async () => {
    renderHome('/app', accountRoutes([mainAccount], MAIN_ID, { portfolio: () => jsonResponse(portfolio({ isComplete: false })) }));

    await findSummary();
    await openAccountDetails();
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

    const summary = await findSummary();
    expect(valueOf(summary, 'Объём позиций')).toBe('13 000 USDT');
    expect(plain(within(summary).getByText(/Long/).textContent)).toContain('Long 10 000 USDT · Short 3 000 USDT');
    expect(screen.getByRole('button', { name: 'Открытые 3' })).toBeVisible();
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

    const summary = await findSummary();
    expect(valueOf(summary, 'Объём позиций')).toBe('5 000 USDC');
    expect(plain(within(summary).getByText(/Long/).textContent)).toContain('Short 0 USDC');
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

    const summary = await findSummary();
    await userEvent.click(within(summary).getByText('Экспозиция', { exact: true }));
    expect(valueOf(within(summary).getByRole('region', { name: 'Экспозиция USDC' }), 'Всего')).toBe('5 000 USDC');
    expect(valueOf(within(summary).getByRole('region', { name: 'Экспозиция USDT' }), 'Всего')).toBe('13 000 USDT');
    expect(plain(summary.textContent)).not.toContain('18 000');
  });
});

describe('HomePage positions', () => {
  it('показывает счётчик портфеля при пустой экспозиции и фильтрации списка', async () => {
    renderHome('/app?side=short', {
      ...accountRoutes([mainAccount], MAIN_ID, {
        portfolio: () => jsonResponse(portfolio({ exposures: [], currentPositionCount: 3, isComplete: false })),
      }),
      [positionsUrl(MAIN_ID, '&side=short')]: () => page([position({ side: 'short' })]),
      [positionsUrl(MAIN_ID, '&trackingState=closed&side=short')]: () => page([]),
    });

    const summary = await findSummary();
    expect(within(summary).getByText('Экспозиция недоступна.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Открытые 3' })).toBeVisible();
    await userEvent.click(screen.getByRole('button', { name: 'Закрытые' }));
    expect(await screen.findByText('Закрытых позиций нет.')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Открытые 3' })).toBeVisible();
  });

  it('показывает текущие позиции с объёмом и PnL в активе расчёта', async () => {
    renderHome('/app', accountRoutes([mainAccount], MAIN_ID, { positions: () => page([position(), ethPosition]) }));

    const btc = (await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).closest('tr')!;
    expect(within(btc).getByText('LONG', { exact: true })).toBeVisible();
    expect(plain(btc.textContent)).toContain('10 000 USDT');
    expect(plain(btc.textContent)).toContain('+230 USDT');
    const eth = screen.getByRole('link', { name: 'ETHUSDC · 2,4 LONG' }).closest('tr')!;
    expect(plain(eth.textContent)).toContain('5 000 USDC');
    expect(plain(eth.textContent)).toContain('+35 USDC');
    expect(screen.getByRole('button', { name: /^Открытые/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('при переходе к закрытым позициям отправляет trackingState=closed и сбрасывает фильтр состояния', async () => {
    const closed = position({ trackingState: 'closed', closedAt: '2026-10-06T12:00:00Z', unrealizedPnl: null });
    const requests = renderHome(`/app?account=${MAIN_ID}&state=stale`, {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID, '&trackingState=stale')]: () => page([]),
      [positionsUrl(MAIN_ID, '&trackingState=closed')]: () => page([closed]),
    });
    await screen.findByText('Текущих позиций нет.');

    await userEvent.click(screen.getByRole('button', { name: 'Закрытые' }));

    const card = await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    expect(within(card.closest('tr')!).getByText('Закрыта', { selector: 'span' })).toBeVisible();
    expect(urls(requests, positionsUrl(MAIN_ID, '&trackingState=closed'))).toHaveLength(1);
    expect(searchParams().get('view')).toBe('closed');
    expect(searchParams().has('state')).toBe(false);
    expect(screen.queryByLabelText('Состояние')).not.toBeInTheDocument();
  });

  it('фильтрует текущие позиции по состоянию', async () => {
    const stale = position({ trackingState: 'stale' });
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID, '&trackingState=stale')]: () => page([stale]),
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.click(screen.getByText('Фильтры', { selector: 'summary' }));
    await userEvent.selectOptions(screen.getByLabelText('Состояние'), 'stale');

    await screen.findByText('Устарела');
    expect(urls(requests, positionsUrl(MAIN_ID, '&trackingState=stale'))).toHaveLength(1);
    expect(searchParams().get('state')).toBe('stale');
  });

  it('фильтрует позиции по направлению', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID, '&side=short')]: () => page([position({ side: 'short' })]),
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.click(screen.getByText('Фильтры', { selector: 'summary' }));
    await userEvent.selectOptions(screen.getByLabelText('Направление'), 'short');

    expect(await screen.findByRole('link', { name: 'BTCUSDT · 0,15 SHORT' })).toBeVisible();
    expect(urls(requests, positionsUrl(MAIN_ID, '&side=short'))).toHaveLength(1);
    expect(searchParams().get('side')).toBe('short');
  });

  it('передаёт серверу точный фильтр по тикеру', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => page([position(), ethPosition]) }),
      [positionsUrl(MAIN_ID, '&symbol=ETHUSDC')]: () => page([ethPosition]),
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.type(screen.getByLabelText('Символ'), '  ETHUSDC ');
    await userEvent.click(screen.getByRole('button', { name: 'Найти' }));

    await waitFor(() =>
      expect(screen.queryByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('link', { name: 'ETHUSDC · 2,4 LONG' })).toBeVisible();
    expect(urls(requests, positionsUrl(MAIN_ID, '&symbol=ETHUSDC'))).toHaveLength(1);
    expect(searchParams().get('symbol')).toBe('ETHUSDC');
  });

  it('повторяет проваленную страницу списка с исходным cursor', async () => {
    const second = position({ id: 'second', symbol: 'SOLUSDT', size: 10 });
    const firstUrl = positionsUrl(MAIN_ID);
    const nextUrl = positionsUrl(MAIN_ID, '&cursor=opaque%2Bcursor%2F%3D');
    let continuationReads = 0;
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => page([position()], 'opaque+cursor/=') }),
      [nextUrl]: () => ++continuationReads === 1 ? new Response(null, { status: 503 }) : page([second]),
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
    await userEvent.click(within(await screen.findByRole('alert')).getByRole('button', { name: 'Повторить' }));

    expect(await screen.findAllByRole('link', { name: 'SOLUSDT · 10 LONG' })).toHaveLength(1);
    expect(screen.getByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(urls(requests, firstUrl)).toHaveLength(1);
    expect(urls(requests, nextUrl)).toHaveLength(2);
  });

  it('сохраняет позиции и cursor после старого отказа продолжения', async () => {
    const pending = deferred<Response>();
    const nextUrl = positionsUrl(MAIN_ID, '&cursor=next');
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => page([position()], 'next') }),
      [nextUrl]: () => pending.promise,
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
    expect(urls(requests, nextUrl)).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: 'Проверить сессию' }));
    await act(async () => pending.resolve(new Response(null, { status: 403 })));

    expect(screen.getByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(urls(requests, '/bff/auth/session')).toHaveLength(2);
    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
    expect(urls(requests, nextUrl)).toHaveLength(2);
  });

  it('добавляет следующую страницу по непрозрачному cursor', async () => {
    const second = position({ id: 'second', symbol: 'SOLUSDT', size: 10 });
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => page([position()], 'opaque+cursor/=') }),
      [positionsUrl(MAIN_ID, '&cursor=opaque%2Bcursor%2F%3D')]: () => page([second]),
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));

    expect(await screen.findByRole('link', { name: 'SOLUSDT · 10 LONG' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
    expect(urls(requests, positionsUrl(MAIN_ID, '&cursor=opaque%2Bcursor%2F%3D'))).toHaveLength(1);
    expect(searchParams().has('cursor')).toBe(false);
  });
});

describe('HomePage manual sync', () => {
  it('синхронизирует с antiforgery и перечитывает подключение, портфель и первую страницу', async () => {
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

    await openAccountDetails();
    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));

    expect(await screen.findByRole('button', { name: 'Синхронизация…' })).toBeDisabled();
    sync.resolve(jsonResponse(mainAccount));
    expect(await screen.findByText('Синхронизация завершена.')).toBeVisible();
    await waitFor(() => expect(screen.getByRole('button', { name: 'Открытые 4' })).toBeVisible());
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

  it('сохраняет подключение, если отказ после синхронизации устарел', async () => {
    const pending = deferred<Response>();
    let reads = 0;
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [ACCOUNTS_URL]: () => ++reads === 1 ? jsonResponse({ items: [mainAccount] }) : pending.promise,
      [syncUrl(MAIN_ID)]: () => jsonResponse(mainAccount),
    });
    await findSummary();
    await openAccountDetails();
    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));
    await waitFor(() => expect(reads).toBe(2));
    await userEvent.click(screen.getByRole('button', { name: 'Проверить сессию' }));
    await act(async () => pending.resolve(new Response(null, { status: 403 })));

    expect(screen.getByRole('region', { name: 'Сводка' })).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(urls(requests, '/bff/auth/session')).toHaveLength(2);
    expect(urls(requests, syncUrl(MAIN_ID))).toHaveLength(1);
  });

  it('сохраняет последний портфель и позиции при сбое чтения после синхронизации', async () => {
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

    await findSummary();
    expect(screen.getByRole('button', { name: 'Открытые 3' })).toBeVisible();
    expect(await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Загрузить ещё' })).toBeVisible();

    await openAccountDetails();
    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));

    expect(await screen.findByText('Синхронизация завершена.')).toBeVisible();
    await waitFor(() => {
      expect(portfolioCalls).toBe(2);
      expect(positionsCalls).toBe(2);
    });

    expect(screen.getByRole('button', { name: 'Открытые 3' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
    expect(screen.getAllByText('Сервис временно недоступен. Повторите попытку позже.')).toHaveLength(2);
    expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(2);
    expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(2);
  });

  it('показывает ошибку синхронизации без повтора и перечитывает фактические данные', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [syncUrl(MAIN_ID)]: () => problem(503, 'exchange_unavailable'),
    });
    await findSummary();

    await openAccountDetails();
    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Bybit сейчас недоступен. Повторите попытку позже.');
    await waitFor(() => expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(2));
    expect(urls(requests, syncUrl(MAIN_ID))).toHaveLength(1);
    expect(urls(requests, ACCOUNTS_URL)).toHaveLength(2);
    expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(2);
  });

  it('не перечитывает данные после ответа синхронизации об истечении сессии', async () => {
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [syncUrl(MAIN_ID)]: () => new Response(null, { status: 401 }),
    });
    await findSummary();

    await openAccountDetails();
    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Сессия завершена. Войдите снова.');
    expect(screen.getByRole('button', { name: 'Войти снова' })).toBeVisible();
    expect(urls(requests, ACCOUNTS_URL)).toHaveLength(1);
    expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(1);
  });

  it('игнорирует повторное нажатие во время синхронизации', async () => {
    const sync = deferred<Response>();
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [syncUrl(MAIN_ID)]: () => sync.promise,
    });
    await findSummary();

    await openAccountDetails();
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
    await openAccountDetails();
    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));
    await screen.findByRole('button', { name: 'Синхронизация…' });

    await userEvent.selectOptions(screen.getByLabelText('Подключение'), SECOND_ID);
    await screen.findByRole('link', { name: 'ETHUSDC · 2,4 LONG' });
  }

  it('не показывает результат синхронизации другого счёта и не подменяет данные текущего', async () => {
    const sync = deferred<Response>();
    const requests = renderHome(`/app?account=${MAIN_ID}`, twoAccountRoutes(() => sync.promise));
    await startMainSyncAndSwitchToSecond();

    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Синхронизация…' })).not.toBeInTheDocument();

    sync.resolve(jsonResponse(mainAccount));
    await waitFor(() => expect(urls(requests, ACCOUNTS_URL)).toHaveLength(2));
    await new Promise((settle) => setTimeout(settle, 0));

    expect(searchParams().get('account')).toBe(SECOND_ID);
    expect(screen.getByText('Второй', { selector: 'summary' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Открытые 7' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'ETHUSDC · 2,4 LONG' })).toBeVisible();
    expect(screen.queryByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).not.toBeInTheDocument();
    expect(screen.queryByText('Синхронизация завершена.')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    expect(urls(requests, portfolioUrl(MAIN_ID))).toHaveLength(1);
    expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(1);
    expect(urls(requests, portfolioUrl(SECOND_ID))).toHaveLength(1);
    expect(urls(requests, positionsUrl(SECOND_ID))).toHaveLength(1);

    await userEvent.selectOptions(screen.getByLabelText('Подключение'), MAIN_ID);
    expect(await screen.findByText('Синхронизация завершена.')).toBeVisible();
    expect(screen.getByText('Основной', { selector: 'summary' })).toBeVisible();
  });

  it('не запускает вторую синхронизацию счёта, пока выполняется первая', async () => {
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

  it('не показывает ошибку синхронизации другого счёта', async () => {
    const sync = deferred<Response>();
    const requests = renderHome(`/app?account=${MAIN_ID}`, twoAccountRoutes(() => sync.promise));
    await startMainSyncAndSwitchToSecond();

    sync.resolve(problem(503, 'exchange_unavailable'));
    await waitFor(() => expect(urls(requests, ACCOUNTS_URL)).toHaveLength(2));
    await new Promise((settle) => setTimeout(settle, 0));

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.queryByText('Bybit сейчас недоступен. Повторите попытку позже.')).not.toBeInTheDocument();
    expect(screen.getByText('Второй', { selector: 'summary' })).toBeVisible();
    expect(screen.getByRole('link', { name: 'ETHUSDC · 2,4 LONG' })).toBeVisible();
    expect(screen.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    expect(urls(requests, syncUrl(MAIN_ID))).toHaveLength(1);
  });
});

describe('HomePage async race protection', () => {
  it('не подменяет текущий портфель поздним ответом предыдущего счёта', async () => {
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
    await findSummary();
    lateMain.resolve(jsonResponse(portfolio({ currentPositionCount: 1 })));

    await screen.findByRole('link', { name: 'ETHUSDC · 2,4 LONG' });
    await new Promise((settle) => setTimeout(settle, 0));
    expect(screen.getByRole('button', { name: 'Открытые 7' })).toBeVisible();
    expect(screen.getByText('Второй', { selector: 'summary' })).toBeVisible();
  });

  it('не подменяет текущий список первой страницей прежнего фильтра', async () => {
    const lateAll = deferred<Response>();
    renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => lateAll.promise }),
      [positionsUrl(MAIN_ID, '&side=short')]: () => page([position({ side: 'short' })]),
    });
    await findSummary();

    await userEvent.click(screen.getByText('Фильтры', { selector: 'summary' }));
    await userEvent.selectOptions(screen.getByLabelText('Направление'), 'short');
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 SHORT' });
    lateAll.resolve(page([ethPosition]));

    await new Promise((settle) => setTimeout(settle, 0));
    expect(screen.queryByRole('link', { name: 'ETHUSDC · 2,4 LONG' })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'BTCUSDT · 0,15 SHORT' })).toBeVisible();
  });

  it('не продолжает список по cursor до перечитывания после синхронизации', async () => {
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
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
    await screen.findByRole('link', { name: 'ETHUSDC · 2,4 LONG' });

    await openAccountDetails();
    await userEvent.click(screen.getByRole('button', { name: 'Синхронизировать' }));
    await waitFor(() => expect(urls(requests, positionsUrl(MAIN_ID))).toHaveLength(2));

    expect(screen.getByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).toBeVisible();
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();

    reload.resolve(page([position({ id: 'fresh', symbol: 'SOLUSDT', side: 'short' })], 'fresh-next'));
    await screen.findByRole('link', { name: 'SOLUSDT · 0,15 SHORT' });
    expect(within(screen.getByRole('table', { name: 'Позиции' })).getAllByRole('link')).toHaveLength(1);
    expect(screen.getByRole('button', { name: 'Загрузить ещё' })).toBeEnabled();
    expect(urls(requests, positionsUrl(MAIN_ID, '&cursor=stale'))).toHaveLength(0);
  });

  it('не добавляет запоздалую страницу после смены счёта', async () => {
    const lateMore = deferred<Response>();
    renderHome(`/app?account=${MAIN_ID}`, {
      [ACCOUNTS_URL]: () => jsonResponse({ items: [mainAccount, secondAccount] }),
      [portfolioUrl(MAIN_ID)]: () => jsonResponse(portfolio()),
      [portfolioUrl(SECOND_ID)]: () => jsonResponse(portfolio({}, SECOND_ID)),
      [positionsUrl(MAIN_ID)]: () => page([position()], 'next'),
      [positionsUrl(MAIN_ID, '&cursor=next')]: () => lateMore.promise,
      [positionsUrl(SECOND_ID)]: () => page([ethPosition]),
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });

    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
    await userEvent.selectOptions(screen.getByLabelText('Подключение'), SECOND_ID);
    await screen.findByRole('link', { name: 'ETHUSDC · 2,4 LONG' });
    lateMore.resolve(page([position({ id: 'late', symbol: 'LATEUSDT' })]));

    await new Promise((settle) => setTimeout(settle, 0));
    expect(screen.queryByText(/LATEUSDT/)).not.toBeInTheDocument();
    expect(within(screen.getByRole('table', { name: 'Позиции' })).getAllByRole('link')).toHaveLength(1);
  });
});

describe('HomePage position navigation', () => {
  it('показывает в карточке один принятый снимок, даже если список ещё хранит прежние значения', async () => {
    const btc = position();
    const detail = deferred<Response>();
    const positionPath = '/bff/me/positions/' + btc.id;
    let lost = false;
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount]),
      [positionPath]: () => lost ? problem(404, 'resource_not_found') : detail.promise,
      [positionPath + '/market']: () => jsonResponse(marketFixture()),
      [positionPath + '/candles?interval=1h&limit=200']: () => jsonResponse(candlesFixture()),
      [positionPath + '/evaluation']: () => new Response(null, { status: 204 }),
    });

    const ticker = await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    await userEvent.click(ticker);
    const card = screen.getByRole('region', { name: 'BTCUSDT' });
    await within(card).findByText('Загружаем позицию…');
    expect(within(card).queryByText(/LONG|10 000/)).not.toBeInTheDocument();

    await act(async () => detail.resolve(jsonResponse(positionFixture({
      side: 'short', positionValue: 432, unrealizedPnl: -11, averageEntryPrice: null, markPrice: 0.958,
    }))));
    expect(await within(card).findByText('SHORT · 432 USDT')).toBeVisible();
    const metrics = within(card).getByLabelText('Краткие параметры позиции');
    expect(valueOf(metrics, 'PnL')).toBe('−11 USDT');
    expect(valueOf(metrics, 'Вход')).toBe('—');
    expect(valueOf(metrics, 'Mark позиции')).toBe('0,958');
    expect(ticker).toHaveAccessibleName('BTCUSDT · 0,15 LONG');

    lost = true;
    act(() => emitRealtime({ type: 'position.updated', positionId: btc.id }));
    await waitFor(() => expect(urls(requests, positionPath)).toHaveLength(2));
    await screen.findByText('Позиция недоступна.');
    expect(screen.queryByText('SHORT · 432 USDT')).not.toBeInTheDocument();
    expect(ticker).toHaveFocus();
  });

  it('не перезапускает чтение карточки при изменении списка и возвращает фокус к его заголовку, если строка исчезла', async () => {
    const btc = position();
    const positionPath = `/bff/me/positions/${btc.id}`;
    let items = [btc, ethPosition];
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => page(items) }),
      [positionPath]: () => jsonResponse(positionFixture({ id: btc.id, exchangeAccountId: MAIN_ID })),
      [`${positionPath}/market`]: () => jsonResponse(marketFixture()),
      [`${positionPath}/candles?interval=1h&limit=200`]: () => jsonResponse(candlesFixture()),
      [`${positionPath}/evaluation`]: () => new Response(null, { status: 204 }),
    });

    await userEvent.click(await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' }));
    await screen.findByRole('button', { name: 'Оценить позицию' });
    items = [ethPosition];
    act(() => emitRealtime({ type: 'position.updated', positionId: ethPosition.id }));
    await waitFor(() => expect(screen.queryByRole('link', { name: 'BTCUSDT · 0,15 LONG' })).not.toBeInTheDocument());
    expect(urls(requests, positionPath)).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: 'Закрыть карточку позиции' }));
    expect(screen.getByRole('heading', { name: 'Позиции' })).toHaveFocus();
  });

  it('подтверждает прежний POST только для текущей карточки после переходов A → B → A', async () => {
    const btc = position();
    const btcPath = `/bff/me/positions/${btc.id}`;
    const ethPath = `/bff/me/positions/${ethPosition.id}`;
    const lateEth = deferred<Response>();
    const post = deferred<Response>();
    let btcReads = 0;
    const routesForPosition = (item: PositionListItem, path: string) => ({
      [path]: () => jsonResponse(positionFixture({ id: item.id, symbol: item.symbol, exchangeAccountId: MAIN_ID })),
      [`${path}/market`]: () => jsonResponse(marketFixture({ symbol: item.symbol })),
      [`${path}/candles?interval=1h&limit=200`]: () => jsonResponse(candlesFixture()),
    });
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, { positions: () => page([btc, ethPosition]) }),
      ...routesForPosition(btc, btcPath),
      ...routesForPosition(ethPosition, ethPath),
      [`${btcPath}/evaluation`]: (init) => {
        if (init?.method === 'POST') return post.promise;
        return ++btcReads === 3 ? jsonResponse(evaluationFixture()) : new Response(null, { status: 204 });
      },
      [`${ethPath}/evaluation`]: () => lateEth.promise,
    });

    const btcTicker = await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    await userEvent.click(btcTicker);
    await userEvent.click(await screen.findByRole('button', { name: 'Оценить позицию' }));
    await userEvent.click(screen.getByRole('link', { name: 'ETHUSDC · 2,4 LONG' }));
    await waitFor(() => expect(urls(requests, `${ethPath}/evaluation`)).toHaveLength(1));
    await userEvent.click(btcTicker);
    expect(await screen.findByRole('button', { name: 'Оценка выполняется…' })).toBeDisabled();
    await waitFor(() => expect(btcReads).toBe(2));

    await act(async () => { post.resolve(jsonResponse(evaluationFixture())); });
    await act(async () => { lateEth.resolve(new Response(null, { status: 204 })); });
    const active = screen.getByRole('region', { name: 'BTCUSDT' });
    expect(await within(active).findByRole('button', { name: 'Переоценить' })).toBeEnabled();
    expect(screen.queryByRole('region', { name: 'ETHUSDC' })).not.toBeInTheDocument();
    expect(urls(requests, `${btcPath}/evaluation`).filter((request) => request.init?.method === 'POST')).toHaveLength(1);
    expect(btcReads).toBe(3);
  });

  it('открывает карточку в обзоре и возвращает фокус к позиции с теми же счётом и фильтрами', async () => {
    const btc = position();
    const positionPath = `/bff/me/positions/${btc.id}`;
    const pending = () => new Promise<Response>(() => {});
    const requests = renderHome(`/app?account=${MAIN_ID}&side=long`, {
      ...accountRoutes([mainAccount], MAIN_ID),
      [positionsUrl(MAIN_ID, '&side=long')]: () => page([btc]),
      [positionPath]: () => jsonResponse(positionFixture({ id: btc.id, exchangeAccountId: MAIN_ID, size: 0.15 })),
      [`${positionPath}/market`]: pending,
      [`${positionPath}/candles?interval=1h&limit=200`]: pending,
      [`${positionPath}/evaluation`]: pending,
    });

    const link = await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    expect(link).toHaveAttribute('href', `/app/positions/${btc.id}`);
    await userEvent.click(link);

    expect(await screen.findByRole('heading', { level: 2, name: 'BTCUSDT' })).toBeVisible();
    await userEvent.click(screen.getByRole('button', { name: 'Закрыть карточку позиции' }));
    expect(link).toHaveFocus();

    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    expect(searchParams().get('account')).toBe(MAIN_ID);
    expect(searchParams().get('side')).toBe('long');
    expect(requests.some((request) => request.url.includes('/sync') || request.init?.method === 'POST')).toBe(false);
  });

  it.each(['поздний', 'подготовленный', 'отказ возобновления', 'пауза'] as const)('подтверждает оценку после повторного открытия: %s', async (timing) => {
    const btc = position();
    const positionPath = `/bff/me/positions/${btc.id}`;
    const evaluationUrl = `${positionPath}/evaluation`;
    const empty = () => new Response(null, { status: 204 });
    const post = deferred<Response>();
    const resumeEvaluation = deferred<Response>();
    const resumePortfolio = deferred<Response>();
    let evaluationReads = 0;
    let portfolioReads = 0;
    const requests = renderHome('/app', {
      ...accountRoutes([mainAccount], MAIN_ID, {
        portfolio: () => ++portfolioReads === 2 ? resumePortfolio.promise : jsonResponse(portfolio()),
      }),
      [positionPath]: () => jsonResponse(positionFixture({ id: btc.id, exchangeAccountId: MAIN_ID })),
      [`${positionPath}/market`]: () => jsonResponse(marketFixture()),
      [`${positionPath}/candles?interval=1h&limit=200`]: () => jsonResponse(candlesFixture()),
      [evaluationUrl]: (init) => {
        if (init?.method === 'POST') return post.promise;
        evaluationReads += 1;
        if (evaluationReads === 3 && timing !== 'пауза') return resumeEvaluation.promise;
        if (evaluationReads === 3) return jsonResponse(evaluationFixture());
        if (evaluationReads > 3) return jsonResponse(evaluationFixture());
        return empty();
      },
    });
    const ticker = await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    await userEvent.click(screen.getByRole('button', { name: 'Приостановить автообновление' }));
    await userEvent.click(ticker);
    await userEvent.click(await screen.findByRole('button', { name: 'Оценить позицию' }));
    await userEvent.click(screen.getByRole('button', { name: 'Закрыть карточку позиции' }));
    await userEvent.click(ticker);
    expect(await screen.findByRole('button', { name: 'Оценка выполняется…' })).toBeDisabled();
    await waitFor(() => expect(evaluationReads).toBe(2));
    if (timing === 'пауза') {
      await act(async () => { post.resolve(jsonResponse(evaluationFixture())); });
      expect(await screen.findByRole('button', { name: 'Переоценить' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Возобновить автообновление' })).toBeEnabled();
      expect(urls(requests, evaluationUrl).filter((request) => request.init?.method === 'POST')).toHaveLength(1);
      expect(evaluationReads).toBe(3);
      return;
    }
    await userEvent.click(screen.getByRole('button', { name: 'Возобновить автообновление' }));
    await waitFor(() => expect(evaluationReads).toBe(3));

    if (timing === 'подготовленный') {
      await act(async () => { resumeEvaluation.resolve(empty()); });
    }
    await act(async () => { post.resolve(jsonResponse(evaluationFixture())); });
    expect(screen.getByText('Возобновляем автообновление…')).toBeVisible();
    expect(screen.queryByText(/Оценка ещё не выполнялась/)).not.toBeInTheDocument();
    expect(evaluationReads).toBe(3);
    if (timing !== 'подготовленный') {
      await act(async () => { resumeEvaluation.resolve(empty()); });
    }
    await act(async () => {
      resumePortfolio.resolve(timing === 'отказ возобновления' ? problem(503, 'service_unavailable') : jsonResponse(portfolio()));
    });
    if (timing === 'отказ возобновления') {
      expect(await screen.findByText('Не удалось возобновить автообновление. Повторите попытку.')).toBeVisible();
    } else {
      expect(await screen.findByRole('button', { name: 'Приостановить автообновление' })).toBeVisible();
    }
    expect(await screen.findByRole('button', { name: 'Переоценить' })).toBeEnabled();
    expect(screen.queryByText(/Оценка ещё не выполнялась/)).not.toBeInTheDocument();
    expect(urls(requests, evaluationUrl).filter((request) => request.init?.method === 'POST')).toHaveLength(1);
    expect(evaluationReads).toBe(4);
  });
});


describe('realtime overview invalidation', () => {
  it('refreshes the selected portfolio and conservatively refreshes the position list', async () => {
    const requests = renderHome(`/app?account=${MAIN_ID}`, accountRoutes([mainAccount]));
    await waitFor(() => expect(requests.some((request) => request.url === positionsUrl(MAIN_ID))).toBe(true));
    const count = (url: string) => requests.filter((request) => request.url === url).length;
    const portfolioBefore = count(portfolioUrl(MAIN_ID));
    const positionsBefore = count(positionsUrl(MAIN_ID));
    expect(screen.queryByText('Список позиций обновлён.')).not.toBeInTheDocument();
    act(() => emitRealtime({ type: 'portfolio.updated', exchangeAccountId: SECOND_ID }));
    expect(count(portfolioUrl(MAIN_ID))).toBe(portfolioBefore);
    act(() => {
      emitRealtime({ type: 'portfolio.updated', exchangeAccountId: MAIN_ID });
      emitRealtime({ type: 'position.updated', positionId: 'previously-unknown' });
    });
    await waitFor(() => expect(count(portfolioUrl(MAIN_ID))).toBe(portfolioBefore + 1));
    await waitFor(() => expect(count(positionsUrl(MAIN_ID))).toBe(positionsBefore + 1));
    expect(await screen.findByText('Список позиций обновлён.')).toHaveAttribute('role', 'status');
    act(() => emitRealtime({ type: 'position.updated', positionId: 'another-update' }));
    await waitFor(() => expect(count(positionsUrl(MAIN_ID))).toBe(positionsBefore + 2));
    expect(screen.queryByText('Обновлён список позиций.')).not.toBeInTheDocument();
    expect(requests.some((request) => request.init?.method === 'POST')).toBe(false);
  });

  it('не объявляет обновление позиций при ошибке', async () => {
    let calls = 0;
    renderHome(`/app?account=${MAIN_ID}`, {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID)]: () => {
        calls += 1;
        return calls === 1 ? page([position()]) : problem(503, 'service_unavailable');
      },
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    act(() => emitRealtime({ type: 'position.updated', positionId: 'changed' }));
    await screen.findByText('Сервис временно недоступен. Повторите попытку позже.');
    expect(screen.queryByText('Список позиций обновлён.')).not.toBeInTheDocument();
  });

  it('объявляет последующее realtime-обновление после интервала тишины', async () => {
    renderHome(`/app?account=${MAIN_ID}`, accountRoutes([mainAccount]));
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    act(() => emitRealtime({ type: 'position.updated', positionId: 'first' }));
    await screen.findByText('Список позиций обновлён.');
    const clock = vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 10_001);
    try {
      act(() => emitRealtime({ type: 'position.updated', positionId: 'second' }));
      expect(await screen.findByText('Обновлён список позиций.')).toHaveAttribute('role', 'status');
    } finally {
      clock.mockRestore();
    }
  });

  it('после интервала тишины объявляет последнее обновление из серии', async () => {
    renderHome(`/app?account=${MAIN_ID}`, accountRoutes([mainAccount]));
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    act(() => emitRealtime({ type: 'position.updated', positionId: 'first' }));
    await screen.findByText('Список позиций обновлён.');
    vi.useFakeTimers();
    try {
      await act(async () => {
        emitRealtime({ type: 'position.updated', positionId: 'second' });
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(screen.queryByText('Обновлён список позиций.')).not.toBeInTheDocument();
      await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
      expect(screen.getByText('Обновлён список позиций.')).toHaveAttribute('role', 'status');
    } finally {
      vi.useRealTimers();
    }
  });

  it('отменяет отложенное объявление при сбое нового обновления', async () => {
    let calls = 0;
    renderHome(`/app?account=${MAIN_ID}`, {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID)]: () => {
        calls += 1;
        return calls < 4 ? page([position()]) : problem(503, 'service_unavailable');
      },
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    act(() => emitRealtime({ type: 'position.updated', positionId: 'first' }));
    await screen.findByText('Список позиций обновлён.');
    vi.useFakeTimers();
    try {
      await act(async () => {
        emitRealtime({ type: 'position.updated', positionId: 'second' });
        await Promise.resolve();
        await Promise.resolve();
      });
      await act(async () => {
        emitRealtime({ type: 'position.updated', positionId: 'third' });
        await Promise.resolve();
        await Promise.resolve();
      });
      expect(calls).toBe(4);
      await act(async () => { await vi.advanceTimersByTimeAsync(10_000); });
      expect(screen.queryByText('Обновлён список позиций.')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });
});


describe('realtime pagination consistency', () => {
  it('отбрасывает старый cursor и объединяет события, пока загружается первая страница по фильтру', async () => {
    const slow = deferred<Response>();
    const final = deferred<Response>();
    let calls = 0;
    const requests = renderHome(`/app?account=${MAIN_ID}&side=long`, {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID, '&side=long')]: () => {
        calls += 1;
        if (calls === 1) return page([position()], 'old-cursor');
        if (calls === 2) return slow.promise;
        if (calls === 3) return final.promise;
        return page([position({ id: 'new', symbol: 'SOLUSDT' })]);
      },
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    expect(screen.getByRole('button', { name: 'Загрузить ещё' })).toBeEnabled();
    act(() => emitRealtime({ type: 'position.updated', positionId: 'new' }));
    await waitFor(() => expect(calls).toBe(2));
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
    act(() => {
      emitRealtime({ type: 'position.updated', positionId: 'old' });
      emitRealtime({ type: 'position.updated', positionId: 'new' });
    });
    expect(calls).toBe(2);
    await act(async () => slow.resolve(page([position({ id: 'stale', symbol: 'STALEUSDT' })], 'stale-cursor')));
    await screen.findByRole('link', { name: 'STALEUSDT · 0,15 LONG' });
    expect(screen.queryByText('Список позиций обновлён.')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
    await act(async () => final.resolve(page([position({ id: 'new', symbol: 'SOLUSDT' })], 'fresh-cursor')));
    await screen.findByRole('link', { name: 'SOLUSDT · 0,15 LONG' });
    expect(await screen.findByText('Список позиций обновлён.')).toHaveAttribute('role', 'status');
    expect(screen.getByRole('button', { name: 'Загрузить ещё' })).toBeEnabled();
    expect(searchParams().get('side')).toBe('long');
    expect(requests.some((request) => request.url.includes('cursor=old-cursor'))).toBe(false);
    expect(calls).toBe(3);
  });
});


describe('recovery of a pending positions page', () => {
  it('сохраняет счёт и фильтр, сбрасывает прежний cursor и игнорирует поздний ответ', async () => {
    const slow = deferred<Response>();
    let calls = 0;
    const requests = renderHome(`/app?account=${MAIN_ID}&side=long`, {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID, '&side=long')]: () => {
        calls += 1;
        if (calls === 1) return page([position()], 'old-cursor');
        if (calls === 2) return slow.promise;
        return page([position({ id: 'new', symbol: 'SOLUSDT' })]);
      },
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    act(() => emitRealtime({ type: 'position.updated', positionId: 'new' }));
    await waitFor(() => expect(calls).toBe(2));
    act(() => emitRealtime({ type: 'recovery' }));
    await screen.findByRole('link', { name: 'SOLUSDT · 0,15 LONG' });
    expect(calls).toBe(3);
    await act(async () => slow.resolve(page([position({ id: 'stale', symbol: 'STALEUSDT' })], 'stale-cursor')));
    expect(screen.queryByText(/STALEUSDT/)).not.toBeInTheDocument();
    expect(searchParams().get('account')).toBe(MAIN_ID);
    expect(searchParams().get('side')).toBe('long');
    expect(requests.some((request) => request.url.includes('cursor=old-cursor'))).toBe(false);
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
  });
});


describe('recovery of closed positions', () => {
  it('сохраняет выбранный счёт, режим закрытых позиций и фильтр', async () => {
    const closed = position({ trackingState: 'closed', closedAt: '2026-10-06T12:00:00Z', unrealizedPnl: null });
    const url = positionsUrl(MAIN_ID, '&trackingState=closed&side=long');
    const requests = renderHome(`/app?account=${MAIN_ID}&view=closed&side=long`, {
      ...accountRoutes([mainAccount]),
      [url]: () => page([closed]),
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    act(() => emitRealtime({ type: 'recovery' }));
    await waitFor(() => expect(urls(requests, url)).toHaveLength(2));
    expect(searchParams().get('account')).toBe(MAIN_ID);
    expect(searchParams().get('view')).toBe('closed');
    expect(searchParams().get('side')).toBe('long');
    expect(screen.getByRole('button', { name: 'Закрытые' })).toHaveAttribute('aria-pressed', 'true');
  });
});


describe('непрерывная инвалидация портфеля', () => {
  it('показывает промежуточный и заключительный снимки выбранного аккаунта', async () => {
    const first = deferred<Response>();
    const second = deferred<Response>();
    let calls = 0;
    const requests = renderHome(`/app?account=${MAIN_ID}`, accountRoutes([mainAccount], MAIN_ID, {
      portfolio: () => ++calls === 1 ? first.promise : calls === 2 ? second.promise : jsonResponse(portfolio({ currentPositionCount: 9 })),
    }));
    await waitFor(() => expect(calls).toBe(1));
    act(() => {
      emitRealtime({ type: 'portfolio.updated', exchangeAccountId: SECOND_ID });
      emitRealtime({ type: 'portfolio.updated', exchangeAccountId: MAIN_ID });
      emitRealtime({ type: 'portfolio.updated', exchangeAccountId: MAIN_ID });
    });
    expect(calls).toBe(1);
    await act(async () => first.resolve(jsonResponse(portfolio({ currentPositionCount: 4 }))));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Открытые 4' })).toBeVisible());
    await waitFor(() => expect(calls).toBe(2));
    act(() => emitRealtime({ type: 'portfolio.updated', exchangeAccountId: MAIN_ID }));
    await act(async () => second.resolve(jsonResponse(portfolio({ currentPositionCount: 7 }))));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Открытые 9' })).toBeVisible());
    expect(calls).toBe(3);
    expect(requests.some((request) => request.init?.method === 'POST')).toBe(false);
  });
});

describe('инвалидация continuation', () => {
  it('не добавляет старую страницу и не возвращает её cursor', async () => {
    const continuation = deferred<Response>();
    const refresh = deferred<Response>();
    let calls = 0;
    const requests = renderHome(`/app?account=${MAIN_ID}`, {
      ...accountRoutes([mainAccount]),
      [positionsUrl(MAIN_ID)]: () => ++calls === 1 ? page([position()], 'old') : refresh.promise,
      [positionsUrl(MAIN_ID, '&cursor=old')]: () => continuation.promise,
    });
    await screen.findByRole('link', { name: 'BTCUSDT · 0,15 LONG' });
    await userEvent.click(screen.getByRole('button', { name: 'Загрузить ещё' }));
    act(() => emitRealtime({ type: 'position.updated', positionId: 'new' }));
    await waitFor(() => expect(calls).toBe(2));
    await act(async () => continuation.resolve(page([position({ id: 'stale', symbol: 'STALEUSDT' })], 'stale')));
    expect(screen.queryByText(/STALEUSDT/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Загрузить ещё' })).not.toBeInTheDocument();
    await act(async () => refresh.resolve(page([position({ id: 'new', symbol: 'SOLUSDT' })], 'fresh')));
    await screen.findByRole('link', { name: 'SOLUSDT · 0,15 LONG' });
    expect(screen.getByRole('button', { name: 'Загрузить ещё' })).toBeEnabled();
    expect(requests.some((request) => request.init?.method === 'POST')).toBe(false);
  });
});
import { MantineProvider } from '@mantine/core';
