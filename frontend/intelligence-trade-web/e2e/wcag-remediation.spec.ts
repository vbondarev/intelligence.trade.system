import { expect, test } from '@playwright/test';
import { candleFixture, candlesFixture, marketFixture, positionFixture } from '../src/test/positionFixtures';
import { loginThroughLanding } from './support';
import { MAIN_ID, mockBusinessData, mockPositionPage, position } from './uiMock';

test('точные малые цены прокручиваются внутри таблицы при 320 CSS px', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await page.addInitScript(() => {
    const original = CanvasRenderingContext2D.prototype.fillText;
    const paints = new Map<string, boolean>();
    Object.defineProperty(window, 'chartPricePaints', { value: paints });
    CanvasRenderingContext2D.prototype.fillText = function (label, x, y, maxWidth?) {
      if (this.canvas.closest('.position-chart')) {
        const pixelRatio = this.canvas.width / this.canvas.getBoundingClientRect().width;
        paints.set(String(label), this.measureText(String(label)).width * pixelRatio <= this.canvas.width);
      }
      if (maxWidth === undefined) original.call(this, label, x, y);
      else original.call(this, label, x, y, maxWidth);
    };
  });
  await page.route('**/bff/me/positions/btc', (route) => route.fulfill({
    json: positionFixture({
      id: 'btc', averageEntryPrice: 3e-20, stopLoss: 2e-20, takeProfit: 4e-20,
      breakEvenPrice: 3e-20, liquidationPrice: 1e-20, markPrice: 3e-20,
    }),
  }));
  const market = marketFixture({ positionId: 'btc' });
  await page.route('**/bff/me/positions/btc/market', (route) => route.fulfill({
    json: { ...market, h1: { ...market.h1, support1: null, resistance1: null } },
  }));
  await page.route('**/bff/me/positions/btc/candles?*', (route) => route.fulfill({
    json: candlesFixture('1h', [{
      ...candleFixture('2026-10-12T08:00:00Z'), open: 2e-20, high: 4e-20, low: 1e-20, close: 3e-20,
    }]),
  }));
  await loginThroughLanding(page);
  await page.getByRole('link', { name: /^BTCUSDT/ }).click();
  await page.getByText('Данные графика и ценовые уровни', { exact: true }).click();
  const region = page.getByRole('region', { name: 'Таблица свечей' });
  const cells = region.getByRole('cell');
  const prefix = '0,' + '0'.repeat(19);
  await expect(cells).toHaveText(['2', '4', '1', '3'].map((digit) => prefix + digit));
  const stopLossPrice = prefix + '2';
  const stopLoss = page.getByRole('row', { name: /^Stop Loss/ }).getByRole('cell');
  await expect(stopLoss).toHaveText(stopLossPrice);
  await expect.poll(() => page.evaluate((label) => {
    const { chartPricePaints } = window as unknown as { chartPricePaints: Map<string, boolean> };
    return { painted: chartPricePaints.has('SL'), fits: chartPricePaints.get(label) };
  }, stopLossPrice)).toEqual({ painted: true, fits: true });
  await region.focus();
  await region.press('ArrowRight');
  await expect.poll(() => region.evaluate((element) => element.scrollLeft)).toBeGreaterThan(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
});

test('поиск сохраняет фокус и восстанавливает символ при Back и Forward', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await loginThroughLanding(page);
  await expect(page.getByText(/^Realtime:/)).toBeHidden();
  await page.getByRole('link', { name: 'Поиск позиций' }).click();
  const input = page.getByRole('textbox', { name: 'Символ' });
  const submit = page.getByRole('button', { name: 'Найти' });
  const result = page.getByRole('region', { name: 'Текущие позиции' });
  await input.fill('BTCUSDT');
  await input.evaluate((node) => node.setAttribute('data-focus-probe', 'original'));
  await expect(input).toHaveAttribute('data-focus-probe', 'original');
  await input.press('Enter');
  await expect(result.getByRole('link', { name: /^BTCUSDT/ })).toBeVisible();
  await expect(input).toBeFocused();
  await expect(input).toHaveAttribute('data-focus-probe', 'original');
  await page.keyboard.press('Tab');
  await expect(submit).toBeFocused();

  await input.fill('ETHUSDC');
  await submit.press('Enter');
  await expect(result.getByRole('link', { name: /^ETHUSDC/ })).toBeVisible();
  await expect(submit).toBeFocused();

  await page.goBack();
  await expect(input).toHaveValue('BTCUSDT');
  await expect(result.getByRole('link', { name: /^BTCUSDT/ })).toBeVisible();
  await page.goForward();
  await expect(input).toHaveValue('ETHUSDC');
  await expect(result.getByRole('link', { name: /^ETHUSDC/ })).toBeVisible();
  await expect(input).toHaveAttribute('data-focus-probe', 'original');
});

function contrast(left: string, right: string) {
  const luminance = (hex: string) => {
    const channels = hex.slice(1).match(/../g)!.map((channel) => parseInt(channel, 16) / 255);
    const [red, green, blue] = channels.map((channel) => channel <= 0.04045
      ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4);
    return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
  };
  const colors = [luminance(left), luminance(right)].sort((a, b) => b - a);
  return (colors[0] + 0.05) / (colors[1] + 0.05);
}

test('canvas рисует контрастные ценовые подписи, выбор имеет отдельный маркер', async ({ page }) => {
  await page.addInitScript(() => {
    const original = CanvasRenderingContext2D.prototype.fillText;
    const paints = new Map<string, string>();
    Object.defineProperty(window, 'chartLabelPaints', { value: paints });
    CanvasRenderingContext2D.prototype.fillText = function (label, x, y, maxWidth?) {
      if (this.canvas.closest('.position-chart') && typeof this.fillStyle === 'string') {
        paints.set(String(label), this.fillStyle);
      }
      if (maxWidth === undefined) original.call(this, label, x, y);
      else original.call(this, label, x, y, maxWidth);
    };
  });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  const selected = page.getByRole('button', { name: 'Текущие', exact: true });
  const unselected = page.getByRole('button', { name: 'Закрытые', exact: true });
  const marker = (element: Element) => getComputedStyle(element, '::after').borderBlockEndWidth;
  await expect(selected).toHaveAttribute('aria-pressed', 'true');
  expect(await selected.evaluate(marker)).toBe('3px');
  expect(await unselected.evaluate(marker)).toBe('0px');
  await selected.focus();
  expect(await selected.evaluate(marker)).toBe('3px');
  expect(contrast('#93c5fd', '#18202b')).toBeGreaterThanOrEqual(3);

  await page.goto('/app/positions/btc');
  await page.locator('summary').filter({ hasText: 'Вид графика' }).click();
  await page.getByLabel('Минимальная цена').fill('40000');
  await page.getByLabel('Максимальная цена').fill('70000');
  await page.getByRole('button', { name: 'Установить диапазон цен' }).click();
  const backgrounds = {
    Entry: '#60a5fa', BreakEven: '#c084fc', SL: '#f87171', TP: '#4ade80',
    Liquidation: '#fb923c', Support: '#22d3ee', Resistance: '#facc15',
  };
  const readPaints = () => page.evaluate(() => Object.fromEntries(
    (window as unknown as { chartLabelPaints: Map<string, string> }).chartLabelPaints,
  ));
  await expect.poll(async () => Object.keys(await readPaints())).toEqual(expect.arrayContaining(Object.keys(backgrounds)));
  const paints = await readPaints();
  for (const [label, background] of Object.entries(backgrounds)) {
    expect(contrast(paints[label], background), label).toBeGreaterThanOrEqual(4.5);
  }
  expect(contrast('#000000', '#4ade80')).toBeGreaterThanOrEqual(4.5);
  expect(contrast('#000000', '#ff9c9c')).toBeGreaterThanOrEqual(4.5);
  expect(contrast('#ffffff', '#253041')).toBeGreaterThanOrEqual(4.5);
});

test('вид графика: шкалы, нативные действия и reflow', async ({ page, hasTouch }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  await page.goto('/app/positions/btc');
  await expect(page.locator('.position-chart canvas').first()).toBeVisible();
  const attribution = page.getByRole('link', { name: 'Графики TradingView' });
  await expect(attribution).toHaveAttribute('href', 'https://www.tradingview.com/');
  await expect(page.getByText(/Copyright © 2025 TradingView/)).toHaveAttribute('lang', 'en');
  const summary = page.locator('summary').filter({ hasText: 'Вид графика' });
  if (hasTouch) await summary.tap();
  else await summary.press('Enter');
  const chart = page.locator('.chart-controls');
  const status = page.locator('.chart-view-status');
  await expect(chart.getByText(/Автомасштаб цен включён/)).toBeVisible();
  const zoom = chart.getByRole('button', { name: 'Увеличить масштаб времени' });
  if (hasTouch) await zoom.tap();
  else {
    await zoom.scrollIntoViewIfNeeded();
    const box = (await zoom.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(1, box.y + box.height / 2);
    await page.mouse.up();
    await expect(status).toBeEmpty();
    await zoom.press('Space');
  }
  await expect(status).toContainText('UTC');
  await chart.getByLabel('Минимальная цена').fill('60000');
  await chart.getByLabel('Максимальная цена').fill('62000');
  await chart.getByRole('button', { name: 'Установить диапазон цен' }).click();
  const auto = chart.getByRole('button', { name: 'Автомасштаб цен' });
  await expect(auto).toHaveAttribute('aria-pressed', 'false');
  await expect(status).toContainText('выключен');
  await auto.click();
  await expect(auto).toHaveAttribute('aria-pressed', 'true');
  await chart.getByRole('button', { name: 'Установить диапазон времени' }).click();
  await expect(chart.getByRole('alert')).toContainText('UTC');
  await chart.getByLabel('Начало диапазона (UTC)').fill('2026-10-08T00:00');
  await chart.getByLabel('Конец диапазона (UTC)').fill('2026-10-09T00:00');
  await chart.getByRole('button', { name: 'Установить диапазон времени' }).press('Enter');
  await expect(chart.getByRole('alert')).toHaveCount(0);
  await expect(status).toContainText(/08\.10\.\d+.*09\.10\.\d+/);
  await chart.getByRole('button', { name: 'Показать все свечи' }).click();
  await expect(status).not.toContainText(/08\.10\.\d+.*09\.10\.\d+/);
  expect(await page.evaluate(() => [window.innerWidth, document.documentElement.scrollWidth])).toEqual([320, 320]);
  const zoomText = await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
  await zoomText.evaluate((style) => style.parentNode?.removeChild(style));
});

test('поиск: общий символ, контекст ссылок, закрытые и независимая пагинация', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await page.route((url) => url.pathname === '/bff/me/positions' && url.searchParams.get('symbol') === 'BTCUSDT'
    && !url.searchParams.has('exchangeAccountId'), async (route) => {
    const params = new URL(route.request().url()).searchParams;
    const closed = params.get('trackingState') === 'closed';
    const continuation = params.has('cursor');
    const items = closed
      ? [position('closed-btc', 'BTCUSDT', 'long', 'USDT', { trackingState: 'closed', closedAt: '2026-10-05T10:00:00Z' })]
      : continuation
        ? [position('more-btc', 'BTCUSDT', 'short', 'USDT')]
        : [position('btc', 'BTCUSDT', 'long', 'USDT'), position('btc-other', 'BTCUSDT', 'long', 'USDC',
          { exchangeAccountId: '6b1d2c3e-4f5a-4b6c-8d7e-9f0a1b2c3d4e' })];
    const hasMore = !closed && !continuation;
    await route.fulfill({ json: { items, hasMore, nextCursor: hasMore ? 'current-next' : null } });
  });
  await loginThroughLanding(page);
  await expect(page.getByRole('link', { name: /^BTCUSDT/ })).toHaveAttribute('href', '/app/positions/btc');
  await page.getByRole('link', { name: 'Поиск позиций' }).click();
  const sections = page.getByRole('navigation', { name: 'Разделы приложения' });
  await expect(sections.getByRole('link', { name: 'Обзор' })).toHaveAttribute('href', '/app');
  await expect(sections.getByRole('link', { name: 'Подключения' })).toHaveAttribute('href', '/app/settings/connections');
  await page.getByLabel('Символ').fill('BTCUSDT');
  await page.getByRole('button', { name: 'Найти' }).click();
  const current = page.getByRole('region', { name: 'Текущие позиции' });
  const primary = current.getByRole('link', { name: /BTCUSDT.*LONG.*Основной.*USDT/ });
  await expect(primary).toHaveAttribute('href', '/app/positions/btc');
  await expect(current.getByRole('link', { name: /BTCUSDT.*USDC/ })).toHaveAttribute('href', '/app/positions/btc-other');
  await current.getByRole('button', { name: 'Загрузить ещё' }).click();
  await expect(current.getByRole('link', { name: /BTCUSDT.*short/i })).toHaveAttribute('href', '/app/positions/more-btc');
  await expect(page.getByRole('region', { name: 'Закрытые позиции' }).getByRole('link', { name: /BTCUSDT/ }))
    .toHaveAttribute('href', '/app/positions/closed-btc');
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
  await primary.click();
  await page.getByRole('link', { name: /К результатам поиска/ }).click();
  await expect(page.getByLabel('Символ')).toHaveValue('BTCUSDT');
  await expect(primary).toBeVisible();
});

test('перетаскивание и колесо используют ту же шкалу, снимок вида можно восстановить', async ({ page, hasTouch }) => {
  test.skip(hasTouch, 'Физические события мыши и колеса');
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  await page.goto('/app/positions/btc');
  await page.locator('summary').filter({ hasText: 'Вид графика' }).click();
  const chart = page.locator('.chart-controls');
  const status = page.locator('.chart-view-status');
  const undo = chart.getByRole('button', { name: 'Отменить изменение вида' });
  const canvas = page.locator('.position-chart canvas').first();
  await expect(chart.getByText(/Автомасштаб цен включён/)).toBeVisible();
  await chart.getByRole('button', { name: 'Увеличить масштаб времени' }).click();
  await expect(status).toContainText('UTC');
  const before = await status.textContent();
  await canvas.scrollIntoViewIfNeeded();
  const box = (await canvas.boundingBox())!;
  const x = box.x + box.width / 2;
  const y = box.y + box.height / 2;
  await page.mouse.move(x, y);
  await page.mouse.wheel(0, -420);
  await expect(undo).toBeEnabled();
  await expect(status).not.toHaveText(before!);
  await undo.click();
  await expect(undo).toBeDisabled();
  await expect(status).toHaveText(before!);
  await canvas.scrollIntoViewIfNeeded();
  const nextBox = (await canvas.boundingBox())!;
  await page.mouse.move(nextBox.x + nextBox.width * 0.5, nextBox.y + nextBox.height * 0.5);
  await page.mouse.down();
  await page.mouse.move(nextBox.x + nextBox.width * 0.7, nextBox.y + nextBox.height * 0.5, { steps: 10 });
  await page.mouse.up();
  await expect(undo).toBeEnabled();
  await undo.click();
  await expect(status).toHaveText(before!);

  await chart.getByLabel('Минимальная цена').fill('55000');
  await chart.getByLabel('Максимальная цена').fill('68000');
  await chart.getByRole('button', { name: 'Установить диапазон цен' }).click();
  const auto = chart.getByRole('button', { name: 'Автомасштаб цен' });
  await expect(auto).toHaveAttribute('aria-pressed', 'false');
  const customPrice = await status.textContent();
  const area = page.locator('.position-chart');
  await area.scrollIntoViewIfNeeded();
  const bounds = (await area.boundingBox())!;
  await page.mouse.dblclick(bounds.x + bounds.width - 18, bounds.y + bounds.height / 2);
  await expect(auto).toHaveAttribute('aria-pressed', 'true');
  await expect(undo).toBeEnabled();
  await undo.click();
  await expect(auto).toHaveAttribute('aria-pressed', 'false');
  await expect(status).toHaveText(customPrice!);
});

test('pinch меняет вид графика, touch cancellation восстанавливает его', async ({ page, browserName }) => {
  test.skip(browserName !== 'chromium', 'CDP передаёт несколько точек касания только Chromium');
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 2 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  await page.goto('/app/positions/btc');
  await page.locator('summary').filter({ hasText: 'Вид графика' }).click();
  const chart = page.locator('.chart-controls');
  const status = page.locator('.chart-view-status');
  const undo = chart.getByRole('button', { name: 'Отменить изменение вида' });
  const canvas = page.locator('.position-chart canvas').first();
  await expect(chart.getByText(/Автомасштаб цен включён/)).toBeVisible();
  await chart.getByRole('button', { name: 'Увеличить масштаб времени' }).click();
  await expect(status).toContainText('UTC');
  const before = await status.textContent();

  async function pinch(end: 'touchEnd' | 'touchCancel') {
    await canvas.scrollIntoViewIfNeeded();
    const box = (await canvas.boundingBox())!;
    const x = Math.round(box.x + box.width / 2);
    const y = Math.round(box.y + box.height / 2);
    const points = (distance: number) => [{ id: 1, x: x - distance, y }, { id: 2, x: x + distance, y }];
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: points(30) });
    for (const distance of [45, 60, 85, 110]) {
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: points(distance) });
    }
    await cdp.send('Input.dispatchTouchEvent', { type: end, touchPoints: [] });
  }

  await pinch('touchCancel');
  await expect(status).toHaveText(before!);
  await expect(undo).toBeDisabled();
  await pinch('touchEnd');
  await expect(undo).toBeEnabled();
  await expect(status).not.toHaveText(before!);
  await undo.click();
  await expect(status).toHaveText(before!);
});

test('ошибки формы: знакомые поля получают подсказки, черновик остаётся без ключей', async ({ page }) => {
  await mockBusinessData(page);
  let onlyUnknownFields = false;
  await page.route('**/bff/me/exchange-accounts', async (route) => {
    if (route.request().method() !== 'POST') return route.fallback();
    expect(route.request().headers()['x-csrf-token']).toBeTruthy();
    await route.fulfill({ status: 400, json: {
      code: 'validation_failed',
      detail: 'raw-synthetic-detail',
      errors: onlyUnknownFields ? { bogus: ['raw-synthetic-unknown'] } : {
        displayName: ['raw-synthetic-name'], apiSecret: ['raw-synthetic-secret'], bogus: ['raw-synthetic-unknown'],
      },
    } });
  });
  await loginThroughLanding(page);
  await page.goto(`/app/settings/connections?account=${MAIN_ID}`);
  const form = page.getByRole('form', { name: 'Добавить подключение Bybit' });
  const name = form.getByLabel('Название *');
  const key = form.getByLabel('API key *');
  const secret = form.getByLabel('API secret *');
  await form.getByRole('button', { name: 'Подключить' }).click();
  await expect(key).toHaveAccessibleDescription('Введите API key.');
  await name.fill('Черновик');
  await key.fill('synthetic-key');
  await secret.fill('synthetic-secret');
  await form.getByRole('button', { name: 'Подключить' }).click();
  await expect(name).toHaveAccessibleDescription('Укажите название подключения до 100 символов.');
  await expect(secret).toHaveAccessibleDescription('Введите API secret.');
  await expect(name).toHaveValue('Черновик');
  await expect(key).toHaveValue('');
  await expect(secret).toHaveValue('');
  await expect(key).not.toHaveAttribute('aria-invalid');
  await expect(page.getByText(/raw-synthetic/)).toHaveCount(0);
  await name.fill('Исправлено');
  await expect(name).not.toHaveAttribute('aria-invalid');
  await expect(secret).toHaveAttribute('aria-invalid', 'true');
  onlyUnknownFields = true;
  await key.fill('synthetic-key');
  await secret.fill('synthetic-secret');
  await form.getByRole('button', { name: 'Подключить' }).click();
  const alert = page.getByRole('alert');
  await expect(alert).toContainText('Добавление подключения Bybit');
  await expect(alert).toContainText('Проверьте заполнение полей.');
  await expect(form.locator('[aria-invalid=true]')).toHaveCount(0);
});
