import { expect, test, type Locator, type Page } from '@playwright/test';
import { positionFixture, timelineEvaluationItem, timelinePageFixture, timelineRecommendationItem } from '../src/test/positionFixtures';
import { LONG_DISPLAY_NAME, MAIN_ID, mockBusinessData, mockOverviewDesign, mockPositionPage, position } from './uiMock';
import { identityBaseUrl, loginThroughLanding, submitIdentityLogin } from './support';

const viewports = [
  { name: 'mobile', width: 390, height: 844 },
  { name: 'desktop', width: 1440, height: 900 },
];

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

test('счёт и первая позиция видны на первом экране, а нажатие PnL открывает соседнюю карточку', async ({ page, hasTouch }, testInfo) => {
  test.skip(hasTouch, 'Подсветка при наведении проверяется мышью');
  await page.setViewportSize({ width: 1707, height: 842 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);

  const first = page.getByRole('row', { name: /^BTCUSDT/ });
  const second = page.getByRole('row', { name: /^ETHUSDC/ });
  for (const element of [page.getByText('Общий капитал'), page.getByText('Нереализованный PnL'), first]) {
    await expect(element).toBeInViewport({ ratio: 1 });
  }
  await first.getByRole('cell', { name: /\+230/ }).click();
  const preview = page.getByRole('region', { name: 'BTCUSDT', exact: true });
  const close = preview.getByRole('button', { name: 'Закрыть карточку позиции' });
  await expect(preview.getByRole('heading', { name: 'Оценка и рекомендация' })).toBeVisible();
  expect((await preview.boundingBox())?.x).toBeGreaterThan((await first.boundingBox())!.x);
  await expect(close).not.toBeFocused();
  const selectedColor = await first.evaluate((row) => getComputedStyle(row).backgroundColor);
  await second.hover();
  await expect(first).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
  await expect(second).toHaveCSS('background-color', selectedColor);
  await expect(preview).toBeVisible();

  const ticker = first.getByRole('link', { name: /^BTCUSDT/ });
  await ticker.hover();
  await expect(ticker).toHaveCSS('text-decoration-line', 'none');
  await page.getByRole('heading', { name: 'Обзор' }).hover();
  await expect(first).toHaveCSS('background-color', selectedColor);
  await expectNoHorizontalScroll(page);
  await testInfo.attach('обзор с карточкой 1707×842', { body: await page.screenshot(), contentType: 'image/png' });

  await close.click();
  await ticker.press('Enter');
  await expect(close).toBeFocused();
  await expect(close).toHaveCSS('outline-style', 'solid');
  await close.press('Enter');
  await expect(ticker).toBeFocused();
  const pause = page.getByRole('button', { name: 'Приостановить автообновление' });
  await pause.focus();
  await expect(page.getByRole('tooltip')).toHaveText('Приостановить автообновление');
});

test('контрольный кадр обзора XRPUSDT при 1707×842', async ({ page, isMobile }, testInfo) => {
  test.skip(isMobile, 'Контрольная пара снимается в настольном Chromium');
  await page.setViewportSize({ width: 1707, height: 842 });
  await mockOverviewDesign(page);
  await loginThroughLanding(page);
  const ticker = page.getByRole('link', { name: /^XRPUSDT/ });
  await ticker.click();
  const card = page.getByRole('region', { name: 'XRPUSDT', exact: true });
  await expect(card.getByText('SHORT · 163,2 USDT')).toBeVisible();
  const metrics = card.getByLabel('Краткие параметры позиции');
  await expect(metrics.getByText('−8,24 USDT')).toBeVisible();
  await expect(metrics.getByText('0,9485', { exact: true })).toBeVisible();
  await expect(metrics.getByText('0,957', { exact: true })).toBeVisible();
  await expect(card.getByRole('button', { name: 'Оценить позицию' })).toBeVisible();
  await expect(card.getByRole('region', { name: 'Параметры позиции' })).toBeHidden();
  await expect(card.getByRole('button', { name: 'История позиции' })).toHaveAttribute('aria-expanded', 'false');
  await expect(page.getByRole('button', { name: 'Открытые 3' })).toBeVisible();
  await expect(page.getByRole('region', { name: 'Сводка' })).toContainText(/\$1\s248,57/);
  await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(13, 17, 24)');
  await expect(card).toHaveCSS('background-color', 'rgb(20, 28, 40)');
  await expect(card).toHaveCSS('border-top-color', 'rgb(41, 53, 71)');
  await expect(page.getByText('REST-снимок актуален', { exact: true })).toBeInViewport();
  await expect(card).toBeInViewport({ ratio: 1 });
  const path = testInfo.outputPath('overview-xrpusdt-1707x842.png');
  await page.mouse.move(0, 0);
  await page.screenshot({ path, animations: 'disabled', scale: 'css' });
  await testInfo.attach('Обзор XRPUSDT 1707×842', { path, contentType: 'image/png' });
});

test.describe('выбранная закрытая позиция', () => {
  test.use({ hasTouch: false, isMobile: false });

  test('метка карточки отличается от состояния сделки и остаётся видимой при наведении на другую строку', async ({ page }) => {
    await page.setViewportSize({ width: 1250, height: 842 });
    await mockBusinessData(page);
    await page.route('**/bff/me/positions/closed-long', (route) => route.fulfill({
      json: positionFixture({
        id: 'closed-long', exchangeAccountId: MAIN_ID, symbol: 'XRPUSDT',
        trackingState: 'closed', closedAt: '2026-10-05T10:00:00Z',
      }),
    }));
    await page.route('**/bff/me/positions/closed-long/evaluation', (route) => route.fulfill({ status: 204 }));
    await loginThroughLanding(page);
    await page.getByRole('button', { name: 'Закрытые', exact: true }).click();
    const selected = page.getByRole('row', { name: /^XRPUSDT/ });
    const other = page.getByRole('row', { name: /^ADAUSDT/ });
    const ticker = selected.getByRole('link');
    await selected.getByRole('cell').last().click();
    await page.getByRole('heading', { name: 'Обзор счёта', exact: true }).hover();
    const card = page.getByRole('region', { name: 'XRPUSDT', exact: true });
    await expect(card).toBeVisible();
    const indicator = selected.getByText('Карточка открыта', { exact: true });
    await expect(indicator).toBeVisible();
    expect(await indicator.evaluate((element) => {
      const text = element.firstChild;
      if (text?.nodeType !== Node.TEXT_NODE) return false;
      const word = document.createRange();
      word.setStart(text, 0);
      word.setEnd(text, 'Карточка'.length);
      const { left, top, width, height } = word.getBoundingClientRect();
      return width > 0 && height > 0 && element.contains(document.elementFromPoint(left + width / 2, top + height / 2));
    })).toBe(true);
    await expect(selected.getByText('Закрыта', { exact: true })).toBeVisible();
    await expect(ticker).toHaveAttribute('aria-current', 'true');
    const selectedColor = await selected.evaluate((row) => getComputedStyle(row).backgroundColor);
    await other.hover();
    await expect(selected).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
    await expect(other).toHaveCSS('background-color', selectedColor);
    await expect(indicator).toBeVisible();
    await expect(ticker).toHaveAttribute('aria-current', 'true');
    await expect(card).toBeVisible();

    await page.setViewportSize({ width: 320, height: 842 });
    await expect(ticker).toBeHidden();
    await expectNoHorizontalScroll(page);
    await card.getByRole('button', { name: 'Закрыть карточку позиции' }).click();
    await expect(ticker).toBeVisible();
    await expect(ticker).not.toHaveAttribute('aria-current', 'true');
    await expectNoHorizontalScroll(page);
  });
});

test('граница 680 пикселей измеряется по контейнеру обзора, фокус возвращается к позиции', async ({ page }) => {
  await page.setViewportSize({ width: 980, height: 842 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  const widthStyle = await page.addStyleTag({ content: '.home-page { width: 680px; }' });
  const overview = page.locator('.home-page');
  const ticker = page.getByRole('link', { name: /^BTCUSDT/ });
  await expect(overview).toHaveCSS('width', '680px');
  await ticker.press('Enter');
  const close = page.getByRole('button', { name: 'Закрыть карточку позиции' });
  await expect(close).toBeFocused();
  await expect(ticker).toBeHidden();
  await close.press('Enter');
  await expect(ticker).toBeFocused();
  await widthStyle.evaluate((style) => { style.textContent = '.home-page { width: 681px; }'; });
  await expect(overview).toHaveCSS('width', '681px');
  await ticker.press('Enter');
  await expect(ticker).toBeVisible();
  await expect(close).toBeFocused();
  await close.press('Enter');
  await expect(ticker).toBeFocused();
});

test.describe('подсказка автообновления на узком экране', () => {
  test.use({ hasTouch: false, isMobile: false });

  for (const width of [320, 390]) {
    test(`указатель и Escape управляют подсказкой при ширине ${width}px`, async ({ page }) => {
      await page.setViewportSize({ width, height: 842 });
      await mockBusinessData(page);
      await loginThroughLanding(page);
      await expect(page.getByRole('link', { name: /^BTCUSDT/ })).toBeVisible();
      await expect(page.getByText('REST-снимок устарел', { exact: true })).toBeVisible();
      await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());

      const heading = page.getByRole('heading', { name: 'Обзор счёта', exact: true });
      const body = page.locator('body');
      const pause = page.getByRole('button', { name: 'Приостановить автообновление' });
      const tooltip = page.getByRole('tooltip');
      await expect(body).toBeFocused();
      await expect(tooltip).toBeHidden();
      await pause.hover();
      await expect(tooltip).toBeInViewport({ ratio: 1 });
      await expect(tooltip).toHaveText('Приостановить автообновление');
      await expectNoHorizontalScroll(page);

      const button = (await pause.boundingBox())!;
      const bounds = (await tooltip.boundingBox())!;
      const startX = button.x + button.width / 2;
      const startY = button.y + button.height / 2;
      const x = bounds.x + bounds.width / 2;
      const y = bounds.y + bounds.height / 2;
      const gap = Math.max(bounds.x - (button.x + button.width), button.x - (bounds.x + bounds.width));
      expect(gap).toBeLessThanOrEqual(1);
      for (const step of [0, 0.125, 0.25, 0.5, 0.75, 1]) {
        await page.mouse.move(startX + (x - startX) * step, startY + (y - startY) * step);
        await expect(tooltip).toBeVisible();
      }
      expect(await tooltip.evaluate((element, point) => element.contains(document.elementFromPoint(point.x, point.y)), { x, y })).toBe(true);
      await page.keyboard.press('Escape');
      await expect(tooltip).toBeHidden();
      await expect(body).toBeFocused();

      await pause.hover();
      await expect(tooltip).toBeVisible();
      await heading.hover();
      await expect(tooltip).toBeHidden();

      await page.keyboard.press('Tab');
      await pause.focus();
      await expect(pause).toBeFocused();
      await expect(tooltip).toBeInViewport({ ratio: 1 });
      await page.keyboard.press('Escape');
      await expect(tooltip).toBeHidden();
      await expect(pause).toBeFocused();
      await pause.hover();
      await expect(tooltip).toBeVisible();
      await page.keyboard.press('Escape');
      await expect(tooltip).toBeHidden();
      await expect(pause).toBeFocused();
      await heading.hover();
      await expect(tooltip).toBeHidden();
      await page.keyboard.press('Tab');
      await expect(pause).not.toBeFocused();
      await pause.focus();
      await expect(tooltip).toBeVisible();
      await expect(pause).toBeEnabled();
      await expect(page.getByText(/^Автообновление приостановлено\./)).toHaveCount(0);
    });
  }
});

test('подсказка остаётся в окне, когда снимок счёта сдвигает кнопку с фокусом', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 842 });
  await mockBusinessData(page);
  let releasePortfolio!: () => void;
  const pendingPortfolio = new Promise<void>((resolve) => { releasePortfolio = resolve; });
  await page.route(`**/bff/me/exchange-accounts/${MAIN_ID}/portfolio`, async (route) => {
    await pendingPortfolio;
    await route.fallback();
  });

  try {
    await loginThroughLanding(page);
    await expect(page.getByRole('link', { name: /^BTCUSDT/ })).toBeVisible();
    const freshness = page.getByText('REST-снимок устарел', { exact: true });
    const pause = page.getByRole('button', { name: 'Приостановить автообновление' });
    const tooltip = page.getByRole('tooltip');
    await expect(freshness).toBeHidden();
    await page.keyboard.press('Tab');
    await pause.focus();
    await expect(pause).toBeFocused();
    await expect(tooltip).toBeInViewport({ ratio: 1 });

    releasePortfolio();
    await expect(freshness).toBeVisible();
    await expect(pause).toBeFocused();
    await expect(tooltip).toBeInViewport({ ratio: 1 });
    await expectNoHorizontalScroll(page);
  } finally {
    releasePortfolio();
  }
});

test('при смене режима автообновления подсказка остаётся в окне при ширине 600px', async ({ page }) => {
  await page.setViewportSize({ width: 600, height: 842 });
  await mockBusinessData(page);
  await loginThroughLanding(page);
  await expect(page.getByRole('link', { name: /^BTCUSDT/ })).toBeVisible();
  await expect(page.getByText('REST-снимок устарел', { exact: true })).toBeVisible();

  const pauseLabel = 'Приостановить автообновление';
  const resumeLabel = 'Возобновить автообновление';
  const pause = page.getByRole('button', { name: pauseLabel });
  const resume = page.getByRole('button', { name: resumeLabel });
  const tooltip = page.getByRole('tooltip');
  await page.keyboard.press('Tab');
  await pause.focus();
  await expect(pause).toBeFocused();
  await expect(tooltip).toHaveText(pauseLabel);
  await expect(tooltip).toBeInViewport({ ratio: 1 });

  await page.keyboard.press('Enter');
  await expect(resume).toBeFocused();
  await expect(tooltip).toHaveText(resumeLabel);
  await expect(tooltip).toBeInViewport({ ratio: 1 });

  let releaseResume!: () => void;
  const pendingResume = new Promise<void>((resolve) => { releaseResume = resolve; });
  await page.route('**/bff/me/exchange-accounts', async (route) => {
    await pendingResume;
    await route.fallback();
  }, { times: 1 });

  try {
    await page.keyboard.press('Enter');
    await expect(page.getByText('Возобновляем автообновление…')).toBeVisible();
    await expect(resume).toBeFocused();
    await expect(resume).toBeDisabled();
    await expect(tooltip).toBeInViewport({ ratio: 1 });

    for (const width of [760, 600]) {
      await page.setViewportSize({ width, height: 842 });
      await expect(resume).toBeFocused();
      await expect(tooltip).toBeInViewport({ ratio: 1 });
      await expectNoHorizontalScroll(page);
    }

    releaseResume();
    await expect(pause).toBeFocused();
    await expect(tooltip).toHaveText(pauseLabel);
    await expect(tooltip).toBeInViewport({ ratio: 1 });
  } finally {
    releaseResume();
  }
});

test('узкий обзор возвращает прокрученный список к тикеру и прежней высоте строки', async ({ page, hasTouch }) => {
  await page.setViewportSize({ width: 390, height: 780 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  const positions = Array.from({ length: 28 }, (_, index) => index === 18
    ? position('btc', 'BTCUSDT', 'long', 'USDT')
    : position(`example-${index}`, `TEST${index}USDT`, 'long', 'USDT'));
  await page.route(/\/bff\/me\/positions\?/, (route) => route.fulfill({ json: {
    items: positions, nextCursor: null, hasMore: false,
  } }));
  await loginThroughLanding(page);
  await expect(page).toHaveURL(/\/app\?account=/);
  await page.locator('summary').filter({ hasText: 'Фильтры' }).click();
  await page.getByLabel('Направление').selectOption('long');
  await expect(page).toHaveURL(/[?&]side=long/);
  await expect(page.getByLabel('Направление')).toHaveValue('long');
  const ticker = page.getByRole('link', { name: /^BTCUSDT/ });
  const row = page.getByRole('row', { name: /^BTCUSDT/ });
  await row.scrollIntoViewIfNeeded();
  const top = (await row.boundingBox())!.y;
  expect(await page.evaluate(() => window.scrollY)).toBeGreaterThan(0);
  if (hasTouch) await ticker.tap();
  else await ticker.click();

  const close = page.getByRole('button', { name: 'Закрыть карточку позиции' });
  await expect(close).toBeInViewport();
  await expect(row).not.toBeVisible();
  await close.click();
  await expect(ticker).toBeFocused();
  await expect(page).toHaveURL(/[?&]side=long/);
  await expect(page.getByLabel('Направление')).toHaveValue('long');
  await expect.poll(async () => Math.abs((await row.boundingBox())!.y - top)).toBeLessThanOrEqual(2);
  await expectNoHorizontalScroll(page);
});

for (const viewport of viewports) {
  test(`оболочка доступна на ${viewport.name} ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await mockBusinessData(page);

    await page.goto('/app/settings/connections');
    await expect(page.getByRole('button', { name: 'Войти' })).toBeVisible();
    await expectNoHorizontalScroll(page);

    await page.getByRole('button', { name: 'Войти' }).click();
    await page.waitForURL(url => url.origin === identityBaseUrl && url.pathname === '/account/login');
    await submitIdentityLogin(page);
    await expect(page.getByRole('main')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Выйти' })).toBeVisible();

    const navigation = page.getByRole('navigation', { name: 'Основная навигация' });
    await expect(navigation).toBeVisible();
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
    await expect(page.getByText('REST-снимок устарел', { exact: true })).toBeVisible();

    const exposureToggle = summary.locator('summary').filter({ hasText: 'Экспозиция' });
    await exposureToggle.click();
    await expect(summary.getByRole('region', { name: 'Экспозиция USDT' })).toBeVisible();
    await expect(summary.getByRole('region', { name: 'Экспозиция USDC' })).toBeVisible();
    await exposureToggle.click();
    await expect(page.getByRole('row', { name: /^BTCUSDT/ })).toBeVisible();
    await expect(page.getByRole('row', { name: /^ETHUSDC/ })).toContainText('USDC');
    await page.locator('.overview-account summary').click();
    await expect(page.getByRole('button', { name: 'Синхронизировать' })).toBeEnabled();
    await page.locator('.overview-account summary').click();
    await expectNoHorizontalScroll(page);

    await page.locator('summary').filter({ hasText: 'Фильтры' }).click();
    await page.getByLabel('Направление').selectOption('short');
    await expect(page).toHaveURL(/side=short/);
    await expect(page.getByRole('row', { name: /^SOLUSDT/ })).toBeVisible();
    await expect(page.getByRole('row', { name: /^BTCUSDT/ })).toHaveCount(0);
    await expectNoHorizontalScroll(page);

    await page.getByRole('button', { name: 'Закрытые' }).click();
    await expect(page).toHaveURL(/view=closed/);
    await expect(page).toHaveURL(/side=short/);
    await expect(page).not.toHaveURL(/[?&]state=/);
    await expect(page.getByRole('row', { name: /^ADAUSDT/ })).toBeVisible();
    await expect(page.getByRole('row', { name: /^XRPUSDT/ })).toHaveCount(0);
    await expect(page.getByRole('row', { name: /^SOLUSDT/ })).toHaveCount(0);
    await expect(page.getByLabel('Состояние')).toHaveCount(0);
    await expectNoHorizontalScroll(page);

    await page.getByLabel('Символ').fill('ADAUSDT');
    await page.getByRole('button', { name: 'Найти' }).click();
    await expect(page).toHaveURL(/symbol=ADAUSDT/);
    await expect(page.getByRole('row', { name: /^ADAUSDT/ })).toBeVisible();
    await expectNoHorizontalScroll(page);

    await page.goto('/app/settings/connections');
    await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
    await expectNoHorizontalScroll(page);
  });
}

for (const viewport of viewports) {
  test(`карточка позиции в обзоре доступна на ${viewport.name} ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await mockBusinessData(page);
    const timelineRequests = await mockPositionPage(page);

    await loginThroughLanding(page);
    await page.goto(`/app?account=${MAIN_ID}&side=long`);
    await page.getByRole('link', { name: /^BTCUSDT/ }).click();
    await expect(page).toHaveURL(new RegExp(`/app\\?account=${MAIN_ID}&side=long$`));
    await expect(page.getByRole('heading', { level: 2, name: /^BTCUSDT/ })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Закрыть карточку позиции' })).not.toBeFocused();

    const details = page.locator('summary').filter({ hasText: 'Рыночные данные и параметры' });
    const chart = page.getByRole('group', { name: /^Свечной график BTCUSDT/ });
    await expect(chart).not.toBeVisible();
    await details.click();
    await expect(chart.locator('canvas').first()).toBeVisible();
    const box = await chart.boundingBox();
    expect(box?.width ?? 0).toBeGreaterThan(200);
    expect(box?.height ?? 0).toBeGreaterThan(200);
    await expect(page.getByRole('heading', { name: 'Рыночный контекст' })).toBeVisible();
    await expect(page.getByText('Удерживать')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Переоценить' })).toBeEnabled();
    await expectNoHorizontalScroll(page);

    await page.getByText('Данные графика и ценовые уровни').focus();
    await page.keyboard.press('Enter');
    const candlesTable = page.getByRole('table', { name: /Свечи BTCUSDT, 1h, UTC/ });
    await expect(candlesTable.getByRole('row')).toHaveCount(201);
    await expect(page.getByRole('rowheader', { name: 'Цена входа' })).toBeVisible();

    await page.getByRole('button', { name: '4h' }).click();
    await expect(page).not.toHaveURL(/interval=4h/);
    await expect(page.getByRole('group', { name: /интервал 4h$/ })).toBeVisible();
    await expect(page.getByRole('table', { name: /Свечи BTCUSDT, 4h, UTC/ }).getByRole('row')).toHaveCount(201);
    await details.click();
    await expect(chart).not.toBeVisible();
    await details.click();
    await expect(chart.locator('canvas').first()).toBeVisible();
    await expect(page.getByRole('button', { name: '4h' })).toHaveAttribute('aria-pressed', 'true');
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

    await page.getByRole('button', { name: 'Закрыть карточку позиции' }).click();
    await expect(page.getByRole('link', { name: /^BTCUSDT/ })).toBeFocused();
    await expect(page).toHaveURL(new RegExp(`/app\\?account=${MAIN_ID}&side=long$`));
    await expect(page.getByRole('row', { name: /^BTCUSDT/ })).toBeVisible();
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
