import type { Page } from '@playwright/test';
import type { PositionAction } from '../src/position/positionTypes';
import {
  TIMELINE_EVALUATION,
  TIMELINE_RECOMMENDATION,
  candleFixture,
  candlesFixture,
  continuationTimelinePage,
  evaluationFixture,
  firstTimelinePage,
  marketFixture,
  positionFixture,
  timelinePageFixture,
} from '../src/test/positionFixtures';

export const MAIN_ID = '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6';
export const LONG_DISPLAY_NAME = 'Очень длинное название подключения для проверки переноса'.padEnd(100, 'Ж');

export function position(id: string, symbol: string, side: 'long' | 'short', settlementAsset: string, overrides = {}) {
  return {
    id,
    exchangeAccountId: MAIN_ID,
    symbol,
    side,
    trackingState: 'active',
    size: 0.15,
    averageEntryPrice: 68500,
    markPrice: 70120,
    positionValue: 10000,
    unrealizedPnl: 230,
    leverage: 5,
    liquidationPrice: null,
    firstDetectedAt: '2026-10-01T10:00:00Z',
    lastObservedAt: '2026-10-07T10:00:00Z',
    closedAt: null,
    settlementAsset,
    ...overrides,
  };
}

// Compose stack не применяет business migrations, поэтому подключения, портфель и позиции
// изолированы mock BFF responses на уровне браузера; реальные границы BFF, API и PostgreSQL
// покрыты отдельными tests.
export async function mockBusinessData(page: Page) {
  await page.route('**/bff/me/exchange-accounts', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.abort();
      return;
    }

    await route.fulfill({
      json: {
        items: [
          {
            id: MAIN_ID,
            displayName: 'Основной',
            exchange: 'bybit',
            connectionStatus: 'connected',
            capabilities: ['readBalance', 'readPositions'],
            lastSyncedAt: '2026-10-06T10:00:00Z',
          },
          {
            id: '6b1d2c3e-4f5a-4b6c-8d7e-9f0a1b2c3d4e',
            displayName: LONG_DISPLAY_NAME,
            exchange: 'bybit',
            connectionStatus: 'disabled',
            capabilities: [],
            lastSyncedAt: null,
          },
        ],
      },
    });
  });

  await page.route(`**/bff/me/exchange-accounts/${MAIN_ID}/portfolio`, async (route) => {
    await route.fulfill({
      json: {
        exchangeAccountId: MAIN_ID,
        calculatedAt: '2026-10-06T10:00:00Z',
        capital: { totalEquity: 18420, availableCapital: 7310, totalWalletBalance: 18155, observedAt: '2026-10-06T10:00:00Z' },
        totalUnrealizedPnl: 265,
        positionsFullyReconciled: true,
        isComplete: true,
        isFresh: false,
        currentPositionCount: 3,
        exposures: [
          { settlementAsset: 'USDC', grossExposure: 5000, longExposure: 5000, shortExposure: 0 },
          { settlementAsset: 'USDT', grossExposure: 13000, longExposure: 10000, shortExposure: 3000 },
        ],
      },
    });
  });

  await page.route(/\/bff\/me\/positions\?/, async (route) => {
    const url = new URL(route.request().url());
    const closed = url.searchParams.get('trackingState') === 'closed';
    const side = url.searchParams.get('side');
    const symbol = url.searchParams.get('symbol');
    const closedOverrides = { trackingState: 'closed', closedAt: '2026-10-05T10:00:00Z' };
    const sourceItems = closed
      ? [
          position('closed-long', 'XRPUSDT', 'long', 'USDT', closedOverrides),
          position('closed-short', 'ADAUSDT', 'short', 'USDT', { ...closedOverrides, unrealizedPnl: -4.2 }),
        ]
      : [
          position('btc', 'BTCUSDT', 'long', 'USDT'),
          position('eth', 'ETHUSDC', 'long', 'USDC', { size: 2.4, positionValue: 5000, unrealizedPnl: 35 }),
          position('sol', 'SOLUSDT', 'short', 'USDT', { size: 25, positionValue: 3000, unrealizedPnl: -12.5 }),
        ];
    // Mock повторяет server-side фильтры v1 API одинаково для текущих и закрытых позиций.
    const items = sourceItems.filter(
      (item) => (side === null || item.side === side) && (symbol === null || item.symbol === symbol),
    );
    await route.fulfill({ json: { items, nextCursor: null, hasMore: false } });
  });
}

function hourlyCandles(count: number, intervalHours: number) {
  const end = Date.UTC(2026, 9, 12, 9);
  return Array.from({ length: count }, (_, index) => {
    const openTime = new Date(end - (count - 1 - index) * intervalHours * 3_600_000).toISOString();
    return candleFixture(openTime, 60000 + Math.round(Math.sin(index / 6) * 900 + index * 4));
  });
}

export async function mockPositionPage(page: Page, action: PositionAction = 'hold'): Promise<URL[]> {
  const base = '**/bff/me/positions/btc';
  const timelineRequests: URL[] = [];
  await page.route(base, (route) =>
    route.fulfill({ json: positionFixture({ id: 'btc', exchangeAccountId: MAIN_ID, size: 0.15 }) }),
  );
  await page.route(`${base}/market`, (route) => route.fulfill({ json: marketFixture({ positionId: 'btc' }) }));
  await page.route(`${base}/candles?*`, (route) => {
    const interval = new URL(route.request().url()).searchParams.get('interval') ?? '1h';
    const hours = { '15m': 0.25, '1h': 1, '4h': 4, '1d': 24 }[interval] ?? 1;
    return route.fulfill({ json: candlesFixture(interval, hourlyCandles(200, hours)) });
  });
  await page.route(`${base}/evaluation`, async (route) => {
    if (route.request().method() !== 'GET') {
      await route.abort();
      return;
    }

    const evaluation = evaluationFixture({ positionId: 'btc' });
    if (evaluation.recommendation !== null) {
      evaluation.recommendation.action.value = action;
    }
    await route.fulfill({ json: evaluation });
  });
  await page.route(`${base}/timeline?*`, (route) => {
    const url = new URL(route.request().url());
    timelineRequests.push(url);
    const types = url.searchParams.getAll('type');
    if (types.length > 0) {
      const items = [TIMELINE_RECOMMENDATION, TIMELINE_EVALUATION].filter((item) => types.includes(item.type));
      return route.fulfill({ json: timelinePageFixture(items) });
    }

    const cursor = url.searchParams.get('cursor');
    return route.fulfill({ json: cursor === null ? firstTimelinePage() : continuationTimelinePage() });
  });

  return timelineRequests;
}
