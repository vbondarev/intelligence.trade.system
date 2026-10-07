import { expect, test, type Page } from '@playwright/test';
import { loginThroughLanding } from './support';

const viewports = [
  { name: 'mobile', width: 390, height: 844 },
  { name: 'desktop', width: 1440, height: 900 },
];

const LONG_DISPLAY_NAME = 'Очень длинное название подключения для проверки переноса'.padEnd(100, 'Ж');

async function expectNoHorizontalScroll(page: Page) {
  const overflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.innerWidth);
}

// Compose stack не применяет business migrations, поэтому список подключений изолирован mock BFF response
// на уровне браузера; реальные границы BFF, API и PostgreSQL покрыты отдельными tests.
async function mockConnectionsList(page: Page) {
  await page.route('**/bff/me/exchange-accounts', async (route) => {
    if (route.request().method() !== 'GET') {
      await route.abort();
      return;
    }

    await route.fulfill({
      json: {
        items: [
          {
            id: '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6',
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
}

for (const viewport of viewports) {
  test(`shell is usable on ${viewport.name} ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await mockConnectionsList(page);

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
    await expect(page).toHaveURL(/\/app$/);
    await expect(page.getByRole('heading', { name: 'Обзор' })).toBeVisible();
    await expectNoHorizontalScroll(page);

    await page.goto('/app/settings/connections');
    await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
    await expectNoHorizontalScroll(page);
  });
}
