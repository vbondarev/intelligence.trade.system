import { expect, test, type Locator, type Page } from '@playwright/test';
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
  timelineEvaluationItem,
  timelinePageFixture,
  timelineRecommendationItem,
} from '../src/test/positionFixtures';
import { loginThroughLanding } from './support';

const viewports = [
  { name: 'mobile', width: 390, height: 844 },
  { name: 'desktop', width: 1440, height: 900 },
];

const MAIN_ID = '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6';
const LONG_DISPLAY_NAME = 'Очень длинное название подключения для проверки переноса'.padEnd(100, 'Ж');

async function expectNoHorizontalScroll(page: Page) {
  const overflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.innerWidth);
}

async function expectContainedHorizontally(container: Locator) {
  const overflow = await container.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    const escaping = [...element.querySelectorAll('*')].filter((child) => {
      const box = child.getBoundingClientRect();
      return box.width > 0 && (box.left < bounds.left - 0.5 || box.right > bounds.right + 0.5);
    });
    return {
      scrollWidth: element.scrollWidth,
      clientWidth: element.clientWidth,
      escaping: escaping.map((child) => child.tagName.toLowerCase()),
    };
  });
  expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.clientWidth);
  expect(overflow.escaping).toEqual([]);
}

function position(id: string, symbol: string, side: 'long' | 'short', settlementAsset: string, overrides = {}) {
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
async function mockBusinessData(page: Page) {
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

async function mockPositionPage(page: Page): Promise<URL[]> {
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

    await route.fulfill({ json: evaluationFixture({ positionId: 'btc' }) });
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

for (const viewport of viewports) {
  test(`shell is usable on ${viewport.name} ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await mockBusinessData(page);

    await page.goto('/app/settings/connections');
    await expect(page.getByRole('button', { name: 'Войти' })).toBeVisible();
    await expectNoHorizontalScroll(page);

    await loginThroughLanding(page);
    await expect(page.getByRole('main')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Выйти' })).toBeVisible();

    const navigation = page.getByRole('navigation', { name: 'Основная навигация' });
    await expect(navigation).toBeVisible();
    await navigation.getByRole('link', { name: 'Подключения' }).click();
    await expect(page).toHaveURL(/\/app\/settings\/connections$/);
    await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
    await expect(page.getByRole('article', { name: 'Основной' })).toBeVisible();
    await expect(page.getByRole('article', { name: LONG_DISPLAY_NAME })).toBeVisible();
    await expectNoHorizontalScroll(page);

    await navigation.getByRole('link', { name: 'Обзор' }).click();
    await expect(page).toHaveURL(new RegExp(`/app\\?account=${MAIN_ID}$`));
    await expect(page.getByRole('heading', { name: 'Обзор' })).toBeVisible();

    const selector = page.getByLabel('Подключение');
    await expect(selector).toBeVisible();
    await expect(selector).toHaveValue(MAIN_ID);

    const summary = page.getByRole('region', { name: 'Сводка' });
    await expect(summary).toBeVisible();
    await expect(summary.getByText('Общий капитал')).toBeVisible();
    await expect(summary.getByText('+$265')).toBeVisible();
    await expect(page.getByText('Данные устарели')).toBeVisible();

    const exposure = page.getByRole('region', { name: 'Экспозиция', exact: true });
    await expect(exposure.getByRole('region', { name: 'Экспозиция USDT' })).toBeVisible();
    await expect(exposure.getByRole('region', { name: 'Экспозиция USDC' })).toBeVisible();
    await expect(page.getByRole('article', { name: /^BTCUSDT/ })).toBeVisible();
    await expect(page.getByRole('article', { name: /^ETHUSDC/ })).toContainText('USDC');
    await expect(page.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    await expectNoHorizontalScroll(page);

    await page.getByLabel('Направление').selectOption('short');
    await expect(page).toHaveURL(/side=short/);
    await expect(page.getByRole('article', { name: /^SOLUSDT/ })).toBeVisible();
    await expect(page.getByRole('article', { name: /^BTCUSDT/ })).toHaveCount(0);
    await expectNoHorizontalScroll(page);

    await page.getByRole('button', { name: 'Закрытые' }).click();
    await expect(page).toHaveURL(/view=closed/);
    await expect(page).toHaveURL(/side=short/);
    await expect(page).not.toHaveURL(/[?&]state=/);
    await expect(page.getByRole('article', { name: /^ADAUSDT/ })).toBeVisible();
    await expect(page.getByRole('article', { name: /^XRPUSDT/ })).toHaveCount(0);
    await expect(page.getByRole('article', { name: /^SOLUSDT/ })).toHaveCount(0);
    await expect(page.getByLabel('Состояние')).toHaveCount(0);
    await expectNoHorizontalScroll(page);

    await page.getByLabel('Символ').fill('ADAUSDT');
    await page.getByRole('button', { name: 'Найти' }).click();
    await expect(page).toHaveURL(/symbol=ADAUSDT/);
    await expect(page.getByRole('article', { name: /^ADAUSDT/ })).toBeVisible();
    await expectNoHorizontalScroll(page);

    await page.goto('/app/settings/connections');
    await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
    await expectNoHorizontalScroll(page);
  });
}

for (const viewport of viewports) {
  test(`position page is usable on ${viewport.name} ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await mockBusinessData(page);
    const timelineRequests = await mockPositionPage(page);

    await loginThroughLanding(page);
    await page.goto(`/app?account=${MAIN_ID}&side=long`);
    await page.getByRole('link', { name: /^BTCUSDT/ }).click();
    await expect(page).toHaveURL(/\/app\/positions\/btc$/);
    await expect(page.getByRole('heading', { level: 1, name: /^BTCUSDT/ })).toBeVisible();

    const chart = page.getByRole('group', { name: /^Свечной график BTCUSDT/ });
    await expect(chart.locator('canvas').first()).toBeVisible();
    const box = await chart.boundingBox();
    expect(box?.width ?? 0).toBeGreaterThan(200);
    expect(box?.height ?? 0).toBeGreaterThan(200);
    await expect(page.getByRole('heading', { name: 'Рыночный контекст' })).toBeVisible();
    await expect(page.getByText('Удерживать')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Переоценить' })).toBeEnabled();
    await expectNoHorizontalScroll(page);

    await page.getByRole('button', { name: '4h' }).click();
    await expect(page).toHaveURL(/interval=4h/);
    await expect(page.getByRole('group', { name: /интервал 4h$/ })).toBeVisible();
    await expectNoHorizontalScroll(page);

    const history = page.getByRole('region', { name: 'История позиции' });
    const historyToggle = history.getByRole('button', { name: 'История позиции' });
    await expect(historyToggle).toHaveAttribute('aria-expanded', 'false');
    expect(timelineRequests).toHaveLength(0);

    await historyToggle.click();
    const historyList = history.getByRole('list', { name: 'События истории позиции' });
    const historyEvents = historyList.getByRole('article');
    const eventsOfType = (label: string) => historyList.getByRole('article', { name: new RegExp(`^${label}`) });
    await expect(historyEvents).toHaveCount(3);
    await expect(eventsOfType('Изменение позиции')).toHaveCount(1);
    await expect(eventsOfType('Оценка позиции')).toHaveCount(1);
    await expect(eventsOfType('Рекомендация')).toHaveCount(1);
    expect(timelineRequests.map((url) => url.search)).toEqual(['?pageSize=25']);
    await expectNoHorizontalScroll(page);

    const reduced = eventsOfType('Изменение позиции');
    await reduced.getByText('Подробности').click();
    await expect(reduced.getByLabel('Состояние до изменения')).toBeVisible();
    await expect(reduced.getByLabel('Состояние после изменения')).toBeVisible();
    await expectNoHorizontalScroll(page);

    await history.getByRole('button', { name: 'Показать ещё' }).click();
    await expect(historyEvents).toHaveCount(5);
    await expect(history.getByRole('button', { name: 'Показать ещё' })).toHaveCount(0);
    await expectNoHorizontalScroll(page);

    await history.getByRole('checkbox', { name: 'Изменения позиции' }).uncheck();
    await expect(historyEvents).toHaveCount(2);
    await expect(eventsOfType('Изменение позиции')).toHaveCount(0);
    expect(timelineRequests.at(-1)?.searchParams.getAll('type')).toEqual(['evaluation', 'recommendation']);
    await history.getByRole('checkbox', { name: 'Изменения позиции' }).check();
    await expect(historyEvents).toHaveCount(3);

    const requestsBeforeRefresh = timelineRequests.length;
    await history.getByRole('button', { name: 'Обновить' }).click();
    await expect.poll(() => timelineRequests.length).toBe(requestsBeforeRefresh + 1);
    await expect(historyEvents).toHaveCount(3);
    expect(timelineRequests.at(-1)?.search).toBe('?pageSize=25');
    await expectNoHorizontalScroll(page);

    await expect(chart.locator('canvas').first()).toBeVisible();
    await expect(page.getByText('Удерживать')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Переоценить' })).toBeEnabled();

    await page.getByRole('link', { name: '← К списку позиций' }).click();
    await expect(page).toHaveURL(new RegExp(`/app\\?account=${MAIN_ID}&side=long$`));
    await expect(page.getByRole('article', { name: /^BTCUSDT/ })).toBeVisible();
  });
}

const LONG_RULE_VERSION = `assessment-${'v'.repeat(240)}`;
const LONG_REASON_CODE = `futureReasonCode${'X'.repeat(220)}`;

for (const viewport of viewports) {
  test(`position timeline wraps long backend values on ${viewport.name} ${viewport.width}x${viewport.height}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await mockBusinessData(page);
    await mockPositionPage(page);
    await page.route('**/bff/me/positions/btc/timeline?*', (route) =>
      route.fulfill({
        json: timelinePageFixture([
          timelineRecommendationItem('2026-10-12T08:00:00Z', { reasonCodes: [LONG_REASON_CODE] }),
          timelineEvaluationItem('2026-10-12T07:30:00Z', {
            ruleVersion: LONG_RULE_VERSION,
            reasonCodes: ['portfolioDataStale', LONG_REASON_CODE],
          }),
        ]),
      }),
    );

    await loginThroughLanding(page);
    await page.goto('/app/positions/btc');
    await expect(page.getByRole('heading', { level: 1, name: /^BTCUSDT/ })).toBeVisible();

    const history = page.getByRole('region', { name: 'История позиции' });
    await history.getByRole('button', { name: 'История позиции' }).click();
    const historyList = history.getByRole('list', { name: 'События истории позиции' });
    const evaluation = historyList.getByRole('article', { name: /^Оценка позиции/ });
    const recommendation = historyList.getByRole('article', { name: /^Рекомендация/ });
    await evaluation.getByText('Подробности').click();
    await recommendation.getByText('Подробности').click();

    await expect(evaluation.getByText(LONG_RULE_VERSION)).toBeVisible();
    await expect(evaluation.getByText(LONG_REASON_CODE)).toBeVisible();
    await expect(recommendation.getByText(LONG_REASON_CODE)).toBeVisible();
    await expectContainedHorizontally(evaluation);
    await expectContainedHorizontally(recommendation);
    await expectNoHorizontalScroll(page);
  });
}
