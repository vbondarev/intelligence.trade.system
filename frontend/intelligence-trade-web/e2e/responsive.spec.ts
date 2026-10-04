import { expect, test, type Page } from '@playwright/test';
import { loginThroughLanding } from './support';

const viewports = [
  { name: 'mobile', width: 390, height: 844, navigationVisible: false },
  { name: 'desktop', width: 1440, height: 900, navigationVisible: true },
];

async function expectNoHorizontalScroll(page: Page) {
  const overflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    innerWidth: window.innerWidth,
  }));
  expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.innerWidth);
}

for (const viewport of viewports) {
  test(`shell is usable on ${viewport.name} ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });

    await page.goto('/');
    await expect(page.getByRole('button', { name: 'Войти' })).toBeVisible();
    await expectNoHorizontalScroll(page);

    await loginThroughLanding(page);
    await expect(page.getByRole('main')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Выйти' })).toBeVisible();
    const overviewLink = page.getByRole('link', { name: 'Обзор' });
    if (viewport.navigationVisible) {
      await expect(overviewLink).toBeVisible();
      await overviewLink.click();
      await expect(page).toHaveURL(/\/app$/);
    } else {
      await expect(overviewLink).toBeHidden();
    }

    await expectNoHorizontalScroll(page);
  });
}
