import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { MAIN_ID, mockBusinessData, mockPositionPage } from './uiMock';

async function expectMinimumTargetSize(page: Page) {
  const failures = await page.locator('a, button, input:not([type=hidden]), select, summary').evaluateAll((elements) => {
    const targets = elements.flatMap((element) => {
      if (element instanceof HTMLInputElement && element.disabled) return [];
      const hitArea = element instanceof HTMLInputElement && element.type === 'checkbox'
        ? element.closest('label') ?? element : element;
      const rect = hitArea.getBoundingClientRect();
      return rect.width > 0 && rect.height > 0 ? [{ element, rect }] : [];
    });
    return targets.flatMap(({ element, rect }) => {
      if (rect.width >= 24 && rect.height >= 24) return [];
      const cx = rect.left + rect.width / 2;
      const cy = rect.top + rect.height / 2;
      const overlaps = targets.some(({ element: other, rect: otherRect }) => {
        if (other === element) return false;
        if (otherRect.width < 24 || otherRect.height < 24) {
          const otherCx = otherRect.left + otherRect.width / 2;
          const otherCy = otherRect.top + otherRect.height / 2;
          return Math.hypot(cx - otherCx, cy - otherCy) < 24;
        }
        const dx = Math.max(otherRect.left - cx, 0, cx - otherRect.right);
        const dy = Math.max(otherRect.top - cy, 0, cy - otherRect.bottom);
        return Math.hypot(dx, dy) < 12;
      });
      return overlaps ? [`${element.tagName} ${element.textContent?.trim().slice(0, 40) ?? ''}: ${Math.round(rect.width)}×${Math.round(rect.height)} CSS px`] : [];
    });
  });
  expect(failures).toEqual([]);
}

async function expectNoAxeViolations(page: Page) {
  const result = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(result.violations.map((violation) => violation.id)).toEqual([]);
}

test('открытые формы, обзор и позиция: axe и размер целей на 320 CSS px', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  // Для геометрии используется синтетическая session; real BFF session проверяется отдельно.
  await page.route('**/bff/auth/session', (route) => route.fulfill({ json: {
    authenticated: true,
    user: { userId: 'issue225-fixture-user', subject: 'Тестовый пользователь' },
  } }));
  await page.route('**/bff/auth/session/timing', (route) => {
    const now = Date.now();
    return route.fulfill({ json: {
      serverTimeUtc: new Date(now).toISOString(),
      expiresAtUtc: new Date(now + 8 * 60 * 60 * 1000).toISOString(),
    } });
  });
  await mockBusinessData(page);
  await mockPositionPage(page, 'protectProfit');

  await page.goto('/app/settings/connections');
  const card = page.getByRole('article', { name: 'Основной' });
  await expect(card).toBeVisible();
  await expectMinimumTargetSize(page);
  await expectNoAxeViolations(page);
  await card.getByRole('button', { name: 'Переименовать' }).click();
  await expectMinimumTargetSize(page);
  await expectNoAxeViolations(page);
  await card.getByRole('button', { name: 'Отмена' }).click();
  await card.getByRole('button', { name: 'Заменить ключи' }).click();
  await expectMinimumTargetSize(page);
  await expectNoAxeViolations(page);

  await page.goto(`/app?account=${MAIN_ID}`);
  await expect(page.getByRole('link', { name: /BTCUSDT/ })).toBeVisible();
  await expectMinimumTargetSize(page);
  await expectNoAxeViolations(page);

  await page.getByRole('link', { name: /BTCUSDT/ }).click();
  await expect(page.getByRole('button', { name: 'Закрыть карточку позиции' })).toBeVisible();
  await expectMinimumTargetSize(page);
  await expectNoAxeViolations(page);

  await page.goto('/app/positions/btc');
  await expect(page.getByRole('heading', { level: 1, name: /BTCUSDT/ })).toBeVisible();
  await expect(page.getByText('Защитить прибыль', { exact: true })).toBeVisible();
  await page.getByText('Данные графика и ценовые уровни').click();
  await page.getByRole('button', { name: 'История позиции' }).click();
  await expect(page.getByRole('list', { name: 'События истории позиции' })).toBeVisible();
  await expectMinimumTargetSize(page);
  await expectNoAxeViolations(page);
});
