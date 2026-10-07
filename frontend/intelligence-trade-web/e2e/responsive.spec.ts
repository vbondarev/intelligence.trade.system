import { expect, test, type Page } from '@playwright/test';
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
    const shortOnly = url.searchParams.get('side') === 'short';
    const items = closed
      ? [position('closed-1', 'XRPUSDT', 'long', 'USDT', { trackingState: 'closed', closedAt: '2026-10-05T10:00:00Z' })]
      : [
          position('btc', 'BTCUSDT', 'long', 'USDT'),
          position('eth', 'ETHUSDC', 'long', 'USDC', { size: 2.4, positionValue: 5000, unrealizedPnl: 35 }),
          position('sol', 'SOLUSDT', 'short', 'USDT', { size: 25, positionValue: 3000, unrealizedPnl: -12.5 }),
        ].filter((item) => !shortOnly || item.side === 'short');
    await route.fulfill({ json: { items, nextCursor: null, hasMore: false } });
  });
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
    await expect(page.getByRole('article', { name: /^XRPUSDT/ })).toBeVisible();
    await expect(page.getByLabel('Состояние')).toHaveCount(0);
    await expectNoHorizontalScroll(page);

    await page.getByLabel('Символ').fill('XRPUSDT');
    await page.getByRole('button', { name: 'Найти' }).click();
    await expect(page).toHaveURL(/symbol=XRPUSDT/);
    await expectNoHorizontalScroll(page);

    await page.goto('/app/settings/connections');
    await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
    await expectNoHorizontalScroll(page);
  });
}
