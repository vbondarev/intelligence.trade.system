import { expect, test } from '@playwright/test';
import { loginThroughLanding } from './support';
import { MAIN_ID, mockBusinessData, mockPositionPage } from './uiMock';

test('пустой результат портфеля отличается от временной ошибки', async ({ page }) => {
  await mockBusinessData(page);
  let status = 204;
  await page.route(`**/bff/me/exchange-accounts/${MAIN_ID}/portfolio`, (route) => route.fulfill({ status }));

  await loginThroughLanding(page);
  await expect(page.getByText(/портфель ещё|данных портфеля/i)).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);

  status = 503;
  await page.reload();
  await expect(page.getByRole('alert')).toContainText('Сервис временно недоступен.');
});

test('пустой 401 после переоценки скрывает позицию и оставляет повторный вход', async ({ page }) => {
  await mockBusinessData(page);
  await mockPositionPage(page);
  let attempts = 0;
  await page.route('**/bff/me/positions/btc/evaluation', (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    attempts += 1;
    return route.fulfill({ status: 401 });
  });
  await loginThroughLanding(page);
  await page.getByRole('link', { name: /^BTCUSDT/ }).click();
  await expect(page.getByRole('heading', { name: /BTCUSDT/ })).toBeVisible();
  await page.getByRole('button', { name: 'Переоценить' }).click();
  await expect(page.getByRole('alert')).toContainText('Сессия завершена.');
  await expect(page.getByRole('button', { name: 'Войти снова' })).toBeVisible();
  await expect(page.getByRole('heading', { name: /BTCUSDT/ })).toHaveCount(0);
  expect(attempts).toBe(1);
});
