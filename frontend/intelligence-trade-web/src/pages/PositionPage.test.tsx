import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useLocation, useNavigate, type InitialEntry } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../auth/AuthProvider';
import { AppRoutes } from '../app/router';
import { jsonResponse, mockFetch, type RecordedRequest, type RouteHandler } from '../test/fetchMock';
import {
  ACCOUNT_ID,
  OTHER_POSITION_ID,
  POSITION_ID,
  candleFixture,
  candlesFixture,
  evaluationFixture,
  marketFixture,
  positionFixture,
} from '../test/positionFixtures';

const chart = vi.hoisted(() => ({
  setData: vi.fn(),
  createPriceLine: vi.fn((options: { title: string; price: number }) => options),
  removePriceLine: vi.fn(),
  fitContent: vi.fn(),
  remove: vi.fn(),
}));

// jsdom не реализует canvas: проверяется интеграция компонента с API библиотеки.
vi.mock('lightweight-charts', () => ({
  CandlestickSeries: {},
  ColorType: { Solid: 'solid' },
  LineStyle: { Solid: 0, Dashed: 2 },
  createChart: vi.fn(() => ({
    addSeries: () => ({
      setData: chart.setData,
      createPriceLine: chart.createPriceLine,
      removePriceLine: chart.removePriceLine,
    }),
    timeScale: () => ({ fitContent: chart.fitContent }),
    remove: chart.remove,
  })),
}));

const authenticatedSession = {
  authenticated: true,
  user: { userId: '7a0c8b5e-6c1d-4f3e-9a6b-2d4e8f1a3c5b', subject: '7a0c8b5e-6c1d-4f3e-9a6b-2d4e8f1a3c5b' },
};

const positionUrl = (id = POSITION_ID) => `/bff/me/positions/${id}`;
const marketUrl = (id = POSITION_ID) => `${positionUrl(id)}/market`;
const candlesUrl = (interval = '1h', id = POSITION_ID) => `${positionUrl(id)}/candles?interval=${interval}&limit=200`;
const evaluationUrl = (id = POSITION_ID) => `${positionUrl(id)}/evaluation`;
const pagePath = (id = POSITION_ID) => `/app/positions/${id}`;

function problem(status: number, code: string, extensions: Record<string, unknown> = {}): Response {
  return new Response(
    JSON.stringify({ title: 'Server title must not be shown', status, code, traceId: '00-trace-01', ...extensions }),
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

function positionRoutes(id = POSITION_ID, overrides: Record<string, RouteHandler> = {}): Record<string, RouteHandler> {
  const ownPosition = positionFixture({ id, symbol: id === POSITION_ID ? 'BTCUSDT' : 'ETHUSDT' });
  return {
    [positionUrl(id)]: () => jsonResponse(ownPosition),
    [marketUrl(id)]: () => jsonResponse(marketFixture({ positionId: id, symbol: ownPosition.symbol })),
    [candlesUrl('1h', id)]: () => jsonResponse(candlesFixture('1h')),
    [evaluationUrl(id)]: () => jsonResponse(evaluationFixture({ positionId: id })),
    ...overrides,
  };
}

function LocationProbe() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <output data-testid="location">{location.pathname + location.search}</output>
      <button type="button" onClick={() => navigate(pagePath(OTHER_POSITION_ID))}>
        Открыть другую позицию
      </button>
    </>
  );
}

function renderPage(entry: InitialEntry, routes: Record<string, RouteHandler>): RecordedRequest[] {
  const requests = mockFetch({
    '/bff/auth/session': () => jsonResponse(authenticatedSession),
    '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
    ...routes,
  });
  render(
    <AuthProvider>
      <MemoryRouter initialEntries={[entry]}>
        <AppRoutes />
        <LocationProbe />
      </MemoryRouter>
    </AuthProvider>,
  );
  return requests;
}

const currentLocation = () => screen.getByTestId('location').textContent;
const urls = (requests: RecordedRequest[], url: string) => requests.filter((request) => request.url === url);
const evaluationPosts = (requests: RecordedRequest[], id = POSITION_ID) =>
  urls(requests, evaluationUrl(id)).filter((request) => request.init?.method === 'POST');
const lastChartData = () => chart.setData.mock.calls.at(-1)?.[0] as { close: number }[] | undefined;
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));
const findTitle = (name: string | RegExp) => screen.findByRole('heading', { level: 1, name });
const evaluationPanel = () => screen.getByRole('region', { name: 'Оценка и рекомендация' });

afterEach(() => {
  vi.clearAllMocks();
});

describe('PositionPage loading', () => {
  it('loads position, market, candles and the last evaluation by direct URL without evaluating or syncing', async () => {
    const requests = renderPage(pagePath(), positionRoutes());

    await findTitle('BTCUSDT · 0,5 LONG');
    expect(await screen.findByText('Удерживать')).toBeVisible();
    await screen.findByRole('heading', { name: 'Рыночный контекст' });

    const positionRequests = requests.filter((request) => request.url.startsWith('/bff/me/positions'));
    expect(positionRequests.map((request) => request.url)).toHaveLength(4);
    expect(positionRequests.map((request) => request.url)).toEqual(
      expect.arrayContaining([positionUrl(), marketUrl(), candlesUrl('1h'), evaluationUrl()]),
    );
    expect(positionRequests.every((request) => (request.init?.method ?? 'GET') === 'GET')).toBe(true);
    expect(requests.some((request) => request.url.includes('/sync'))).toBe(false);
    expect(urls(requests, '/bff/auth/antiforgery')).toHaveLength(0);

    await waitFor(() => expect(lastChartData()?.map((candle) => candle.close)).toEqual([60500, 60800, 61000]));
    expect(screen.getByRole('group', { name: 'Свечной график BTCUSDT, интервал 1h' })).toBeVisible();
    const lineTitles = chart.createPriceLine.mock.calls.map(([options]) => options.title);
    expect(lineTitles).toEqual(expect.arrayContaining(['Entry', 'BreakEven', 'SL', 'TP', 'Liquidation', 'Support', 'Resistance']));
    expect(screen.getByRole('button', { name: '1h' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('link', { name: 'Обзор подключения' })).toHaveAttribute('href', `/app?account=${ACCOUNT_ID}`);
  });

  it('shows an unavailable page when the position is not found', async () => {
    renderPage(pagePath(), positionRoutes(POSITION_ID, { [positionUrl()]: () => problem(404, 'resource_not_found') }));

    await findTitle('Позиция недоступна');
    expect(screen.queryByRole('region', { name: 'Оценка и рекомендация' })).not.toBeInTheDocument();
    expect(screen.queryByText('Server title must not be shown')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: '← К списку позиций' })).toHaveAttribute('href', '/app');
  });

  it('keeps other areas when market, candles or evaluation fail independently', async () => {
    renderPage(
      pagePath(),
      positionRoutes(POSITION_ID, {
        [marketUrl()]: () => problem(503, 'market_data_unavailable'),
        [candlesUrl('1h')]: () => problem(503, 'exchange_unavailable'),
        [evaluationUrl()]: () => problem(500, 'internal_error'),
      }),
    );

    await findTitle('BTCUSDT · 0,5 LONG');
    expect(await screen.findByText('Рыночные данные временно недоступны. Повторите попытку позже.')).toBeVisible();
    expect(await screen.findByText('Bybit сейчас недоступен. Повторите попытку позже.')).toBeVisible();
    expect(
      await within(evaluationPanel()).findByText('Не удалось загрузить оценку. Повторите попытку позже.'),
    ).toBeVisible();
    expect(screen.getByText('Средняя цена входа')).toBeVisible();
    expect(screen.getByRole('button', { name: 'Переоценить' })).toBeEnabled();
  });

  it('keeps the page when only the position fails with a server error', async () => {
    renderPage(pagePath(), positionRoutes(POSITION_ID, { [positionUrl()]: () => problem(503, 'service_unavailable') }));

    expect(await screen.findByText('Сервис временно недоступен. Повторите попытку позже.')).toBeVisible();
    expect(await screen.findByText('Удерживать')).toBeVisible();
    expect(screen.queryByText('Позиция недоступна')).not.toBeInTheDocument();
    await findTitle('BTCUSDT');
  });

  it('shows that the position has not been evaluated yet', async () => {
    renderPage(pagePath(), positionRoutes(POSITION_ID, { [evaluationUrl()]: () => new Response(null, { status: 204 }) }));

    expect(await screen.findByText('Оценка ещё не выполнялась. Запустите её кнопкой «Переоценить».')).toBeVisible();
    expect(screen.queryByText('Удерживать')).not.toBeInTheDocument();
  });

  it('shows an empty chart message when there are no candles', async () => {
    renderPage(pagePath(), positionRoutes(POSITION_ID, { [candlesUrl('1h')]: () => jsonResponse(candlesFixture('1h', [])) }));

    expect(await screen.findByText('Свечей для интервала 1h нет.')).toBeVisible();
  });
});

describe('PositionPage navigation', () => {
  it('returns to the list from router state', async () => {
    const returnTo = `/app?account=${ACCOUNT_ID}&side=short`;
    renderPage({ pathname: pagePath(), state: { returnTo } }, positionRoutes());
    await findTitle('BTCUSDT · 0,5 LONG');

    await userEvent.click(screen.getByRole('link', { name: '← К списку позиций' }));

    expect(currentLocation()).toBe(returnTo);
  });

  it('falls back to the account overview for a direct URL or a foreign return path', async () => {
    renderPage({ pathname: pagePath(), state: { returnTo: '//evil.example/app' } }, positionRoutes());
    await findTitle('BTCUSDT · 0,5 LONG');

    expect(screen.getByRole('link', { name: '← К списку позиций' })).toHaveAttribute('href', `/app?account=${ACCOUNT_ID}`);
  });

  it('switches the interval through the URL and keeps the return path', async () => {
    const returnTo = `/app?account=${ACCOUNT_ID}`;
    const requests = renderPage(
      { pathname: pagePath(), state: { returnTo: `${returnTo}&side=long` } },
      positionRoutes(POSITION_ID, {
        [candlesUrl('4h')]: () => jsonResponse(candlesFixture('4h', [candleFixture('2026-10-12T04:00:00Z', 70000)])),
      }),
    );
    await findTitle('BTCUSDT · 0,5 LONG');

    await userEvent.click(screen.getByRole('button', { name: '4h' }));

    await waitFor(() => expect(lastChartData()?.map((candle) => candle.close)).toEqual([70000]));
    expect(currentLocation()).toBe(`${pagePath()}?interval=4h`);
    expect(screen.getByRole('button', { name: '4h' })).toHaveAttribute('aria-pressed', 'true');
    expect(urls(requests, candlesUrl('4h'))).toHaveLength(1);
    expect(urls(requests, positionUrl())).toHaveLength(1);
    expect(urls(requests, evaluationUrl())).toHaveLength(1);
    expect(screen.getByRole('link', { name: '← К списку позиций' })).toHaveAttribute('href', `${returnTo}&side=long`);
  });

  it('normalizes an unsupported interval without requesting it', async () => {
    const requests = renderPage(`${pagePath()}?interval=5m`, positionRoutes());

    await findTitle('BTCUSDT · 0,5 LONG');
    await waitFor(() => expect(currentLocation()).toBe(`${pagePath()}?interval=1h`));
    expect(requests.filter((request) => request.url.includes('/candles')).map((request) => request.url)).toEqual([
      candlesUrl('1h'),
    ]);
  });

  it('does not show late candles of the previous interval', async () => {
    const lateHour = deferred<Response>();
    renderPage(
      pagePath(),
      positionRoutes(POSITION_ID, {
        [candlesUrl('1h')]: () => lateHour.promise,
        [candlesUrl('1d')]: () => jsonResponse(candlesFixture('1d', [candleFixture('2026-10-11T00:00:00Z', 55000)])),
      }),
    );
    await findTitle('BTCUSDT · 0,5 LONG');

    await userEvent.click(screen.getByRole('button', { name: '1d' }));
    await waitFor(() => expect(lastChartData()?.map((candle) => candle.close)).toEqual([55000]));
    lateHour.resolve(jsonResponse(candlesFixture('1h')));
    await settle();

    expect(lastChartData()?.map((candle) => candle.close)).toEqual([55000]);
  });

  it('does not show late responses of the previous position after switching', async () => {
    const latePosition = deferred<Response>();
    const lateEvaluation = deferred<Response>();
    renderPage(pagePath(), {
      ...positionRoutes(POSITION_ID, {
        [positionUrl()]: () => latePosition.promise,
        [evaluationUrl()]: () => lateEvaluation.promise,
      }),
      ...positionRoutes(OTHER_POSITION_ID, {
        [evaluationUrl(OTHER_POSITION_ID)]: () => new Response(null, { status: 204 }),
      }),
    });
    await screen.findByText('Загружаем позицию…');

    await userEvent.click(screen.getByRole('button', { name: 'Открыть другую позицию' }));
    await findTitle('ETHUSDT · 0,5 LONG');
    await screen.findByText('Оценка ещё не выполнялась. Запустите её кнопкой «Переоценить».');
    latePosition.resolve(jsonResponse(positionFixture()));
    lateEvaluation.resolve(jsonResponse(evaluationFixture()));
    await settle();

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('ETHUSDT · 0,5 LONG');
    expect(screen.queryByText('Удерживать')).not.toBeInTheDocument();
  });
});

describe('PositionPage evaluation command', () => {
  it('evaluates with a CSRF token and no body and replaces the evaluation', async () => {
    const evaluate = deferred<Response>();
    const requests = renderPage(
      pagePath(),
      positionRoutes(POSITION_ID, {
        [evaluationUrl()]: (init) =>
          init?.method === 'POST' ? evaluate.promise : jsonResponse(evaluationFixture()),
      }),
    );
    await screen.findByText('Удерживать');

    await userEvent.dblClick(screen.getByRole('button', { name: 'Переоценить' }));
    const running = await screen.findByRole('button', { name: 'Оценка выполняется…' });
    expect(running).toBeDisabled();
    await userEvent.click(running);

    const base = evaluationFixture();
    const recommendation = base.recommendation;
    if (recommendation === null) {
      throw new Error('Fixture must contain a recommendation.');
    }
    evaluate.resolve(
      jsonResponse({ ...base, recommendation: { ...recommendation, action: { ...recommendation.action, value: 'reduce' } } }),
    );

    expect(await screen.findByText('Оценка выполнена.')).toBeVisible();
    expect(screen.getByText('Сократить позицию')).toBeVisible();
    expect(screen.queryByText('Удерживать')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Переоценить' })).toBeEnabled();

    const posts = evaluationPosts(requests);
    expect(posts).toHaveLength(1);
    expect(new Headers(posts[0]?.init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
    expect(posts[0]?.init?.body ?? null).toBeNull();
    expect(urls(requests, '/bff/auth/antiforgery')).toHaveLength(1);
    expect(requests.some((request) => request.url.includes('/sync'))).toBe(false);
  });

  it('shows the not-evaluable reason and keeps the previous evaluation', async () => {
    renderPage(
      pagePath(),
      positionRoutes(POSITION_ID, {
        [evaluationUrl()]: (init) =>
          init?.method === 'POST'
            ? problem(409, 'position_not_evaluable', { reason: 'closedPosition' })
            : jsonResponse(evaluationFixture()),
      }),
    );
    await screen.findByText('Удерживать');

    await userEvent.click(screen.getByRole('button', { name: 'Переоценить' }));

    const alert = await within(evaluationPanel()).findByRole('alert');
    expect(alert).toHaveTextContent('Закрытая позиция не подлежит повторной оценке.');
    expect(alert).toHaveTextContent('00-trace-01');
    expect(screen.getByText('Удерживать')).toBeVisible();
    expect(screen.queryByText('Server title must not be shown')).not.toBeInTheDocument();
  });

  it('offers to log in again when the session ended during evaluation', async () => {
    renderPage(
      pagePath(),
      positionRoutes(POSITION_ID, {
        [evaluationUrl()]: (init) =>
          init?.method === 'POST' ? new Response(null, { status: 401 }) : jsonResponse(evaluationFixture()),
      }),
    );
    await screen.findByText('Удерживать');

    await userEvent.click(screen.getByRole('button', { name: 'Переоценить' }));

    const alert = await within(evaluationPanel()).findByRole('alert');
    expect(alert).toHaveTextContent('Сессия завершена. Войдите снова.');
    expect(within(alert).getByRole('button', { name: 'Войти снова' })).toBeVisible();
  });

  it('does not apply an evaluation result to another position', async () => {
    const evaluate = deferred<Response>();
    const requests = renderPage(pagePath(), {
      ...positionRoutes(POSITION_ID, {
        [evaluationUrl()]: (init) =>
          init?.method === 'POST' ? evaluate.promise : new Response(null, { status: 204 }),
      }),
      ...positionRoutes(OTHER_POSITION_ID, {
        [evaluationUrl(OTHER_POSITION_ID)]: () => new Response(null, { status: 204 }),
      }),
    });
    await screen.findByText('Оценка ещё не выполнялась. Запустите её кнопкой «Переоценить».');
    await userEvent.click(screen.getByRole('button', { name: 'Переоценить' }));
    await screen.findByRole('button', { name: 'Оценка выполняется…' });

    await userEvent.click(screen.getByRole('button', { name: 'Открыть другую позицию' }));
    await findTitle('ETHUSDT · 0,5 LONG');
    expect(screen.getByRole('button', { name: 'Переоценить' })).toBeEnabled();
    evaluate.resolve(jsonResponse(evaluationFixture()));
    await settle();

    expect(screen.queryByText('Оценка выполнена.')).not.toBeInTheDocument();
    expect(screen.queryByText('Удерживать')).not.toBeInTheDocument();
    expect(evaluationPosts(requests, OTHER_POSITION_ID)).toHaveLength(0);
  });
});
