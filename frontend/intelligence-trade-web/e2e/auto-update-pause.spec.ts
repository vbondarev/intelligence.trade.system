import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { positionFixture } from '../src/test/positionFixtures';
import { loginThroughLanding } from './support';
import { MAIN_ID, mockBusinessData, mockPositionPage } from './uiMock';

test('пауза сохраняется при переходе к позиции, ручная история доступна, возобновление можно повторить', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  const timelineReads = await mockPositionPage(page);
  let size = 0.15;
  let failDetail = false;
  await page.route('**/bff/me/positions/btc', (route) => failDetail
    ? route.fulfill({ status: 503 })
    : route.fulfill({ json: positionFixture({ id: 'btc', exchangeAccountId: MAIN_ID, size }) }));
  await loginThroughLanding(page);

  const pause = page.getByRole('button', { name: 'Приостановить автообновление' });
  const resume = page.getByRole('button', { name: 'Возобновить автообновление' });
  await pause.focus();
  await page.keyboard.press('Space');
  await expect(resume).toBeFocused();
  await page.getByRole('link', { name: /^BTCUSDT/ }).click();
  await expect(resume).toBeVisible();
  const title = page.getByRole('heading', { name: /^BTCUSDT/, level: 1 });
  const original = await title.innerText();
  await page.getByText('Данные графика и ценовые уровни').click();
  const candles = page.getByRole('table', { name: /Свечи BTCUSDT/ });
  await expect(candles.getByRole('row')).toHaveCount(201);
  const history = page.getByRole('region', { name: 'История позиции' });
  await history.getByRole('button', { name: 'История позиции' }).click();
  await history.getByRole('button', { name: 'Показать ещё' }).click();
  await expect(history.getByRole('article')).toHaveCount(5);
  expect(timelineReads.some((url) => url.searchParams.has('cursor'))).toBe(true);
  await history.getByRole('button', { name: 'Обновить' }).click();
  await expect(history.getByRole('article')).toHaveCount(3);
  await expect(resume).toBeVisible();
  const audit = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
  expect(audit.violations.map(({ id }) => id)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
  const textZoom = await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
  await textZoom.evaluate((style) => style.parentNode?.removeChild(style));

  size = 2;
  failDetail = true;
  await resume.focus();
  await page.keyboard.press('Enter');
  await expect(page.getByText('Не удалось возобновить автообновление. Повторите попытку.')).toBeVisible();
  await expect(title).toHaveText(original);
  await expect(resume).toBeFocused();

  failDetail = false;
  await page.keyboard.press('Enter');
  await expect(pause).toBeFocused();
  await expect(title).toContainText('2');
  await expect(candles.getByRole('row')).toHaveCount(201);
});

test('logout во время возобновления удаляет снимок и завершает реальную BFF-сессию', async ({ page }) => {
  await mockBusinessData(page);
  await loginThroughLanding(page);
  const position = page.getByRole('link', { name: /^BTCUSDT/ });
  await expect(position).toBeVisible();
  await expect(page.getByText(/^Realtime:/)).toHaveCount(0);
  await page.getByRole('button', { name: 'Приостановить автообновление' }).click();
  let release!: () => void;
  const deferred = new Promise<void>((resolve) => { release = resolve; });
  await page.route('**/bff/me/exchange-accounts', async (route) => {
    await deferred;
    await route.fallback().catch(() => {});
  }, { times: 1 });

  try {
    const reading = page.waitForRequest('**/bff/me/exchange-accounts');
    await page.getByRole('button', { name: 'Возобновить автообновление' }).click();
    await reading;
    await expect(page.getByText('Возобновляем автообновление…')).toBeVisible();
    await expect(position).toBeVisible();
    await page.getByRole('button', { name: 'Выйти' }).click();
    await expect(page.getByRole('button', { name: 'Войти', exact: true })).toBeVisible();
    await expect(position).toHaveCount(0);
    expect(await (await page.request.get('/bff/auth/session')).json()).toEqual({ authenticated: false });
  } finally {
    release();
  }
});
