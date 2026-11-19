import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { loginThroughLanding, openIdentityLoginThroughLanding } from './support';
import { LONG_DISPLAY_NAME, MAIN_ID, mockBusinessData, mockPositionPage } from './uiMock';

const sizes = [
  { name: '320', width: 320, height: 740 },
  { name: '390', width: 390, height: 844 },
  { name: '768', width: 768, height: 1024 },
  { name: '1440', width: 1440, height: 900 },
];

for (const size of sizes) {
  test(`публичная страница: axe и reflow, ${size.name}px`, async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'chromium', 'Матрица размеров выполняется в Chromium');
    await page.setViewportSize(size);
    await page.goto('/');
    await expect(page.getByRole('button', { name: 'Войти' })).toBeVisible();
    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
    expect(results.violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  });
}

const cases = [
  ...sizes.map((size) => ({ ...size, project: 'chromium' })),
  { name: 'iphone-portrait', width: 390, height: 664, project: 'mobile-webkit' },
  { name: 'iphone-landscape', width: 664, height: 390, project: 'mobile-webkit' },
];

for (const viewport of cases) {
  test(`основные страницы: axe и reflow, ${viewport.name}`, async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== viewport.project, 'Проверка выполняется в соответствующем браузере');
    await page.setViewportSize(viewport);
    await mockBusinessData(page);
    await mockPositionPage(page);
    await openIdentityLoginThroughLanding(page);
    const identity = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
    const findings: string[] = identity.violations.map((violation) => `Identity: ${violation.id} ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`);
    await loginThroughLanding(page);
    for (const [path, heading] of [
      ['/app/settings/connections', 'Подключения'],
      [`/app?account=${MAIN_ID}`, 'Обзор'],
      ['/app/positions/btc', 'BTCUSDT'],
    ]) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1, name: new RegExp(`^${heading}`) })).toBeVisible();
      const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
      findings.push(...results.violations.map((violation) => `${path}: ${violation.id} ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`));
      if (await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)) findings.push(`${path}: horizontal overflow`);
      if (path === '/app/positions/btc') {
        await page.getByRole('button', { name: 'История позиции' }).click();
        await expect(page.getByRole('list', { name: 'События истории позиции' })).toBeVisible();
        const history = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa']).analyze();
        findings.push(...history.violations.map((violation) => `История: ${violation.id} ${violation.nodes.map((node) => node.target.join(' ')).join(', ')}`));
      }
    }
    expect(findings).toEqual([]);
  });
}

test('клавиатура: переход к содержимому и навигация', async ({ page }) => {
  await mockBusinessData(page);
  await loginThroughLanding(page);
  await page.keyboard.press('Tab');
  await expect(page.getByRole('link', { name: /перейти к основному содержимому/i })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('main')).toBeFocused();
});

test('фокус переходит к заголовку при смене страницы и сохраняется при смене фильтра', async ({ page }) => {
  await mockBusinessData(page);
  await loginThroughLanding(page);
  await page.getByRole('navigation', { name: 'Основная навигация' }).getByRole('link', { name: 'Подключения' }).click();
  await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeFocused();
  await expect(page).toHaveTitle('Подключения — Intelligence Trade');
  await page.getByRole('navigation', { name: 'Основная навигация' }).getByRole('link', { name: 'Обзор' }).click();
  await expect(page.getByRole('heading', { name: 'Обзор', level: 1 })).toBeFocused();
  const filter = page.getByLabel('Направление');
  await filter.focus();
  await filter.selectOption('short');
  await expect(filter).toBeFocused();
});

test('форма переименования возвращает фокус на исходное действие', async ({ page }) => {
  await mockBusinessData(page);
  await loginThroughLanding(page);
  await page.goto('/app/settings/connections');
  const card = page.getByRole('article', { name: 'Основной' });
  const rename = card.getByRole('button', { name: 'Переименовать' });
  await rename.click();
  await expect(card.getByLabel('Новое название *')).toBeFocused();
  await card.getByRole('button', { name: 'Отмена' }).click();
  await expect(rename).toBeFocused();
});

test('увеличение текста до 200% на узком экране', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await loginThroughLanding(page);
  await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
  await expect(page.getByRole('heading', { name: 'Обзор', level: 1 })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('длинное название подключения и фокус доступны на узком экране', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await loginThroughLanding(page);
  await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
  const select = page.getByLabel('Подключение');
  await expect(page).toHaveURL(new RegExp(`account=${MAIN_ID}`));
  await select.selectOption('6b1d2c3e-4f5a-4b6c-8d7e-9f0a1b2c3d4e');
  await expect(page).toHaveURL(/account=6b1d2c3e-4f5a-4b6c-8d7e-9f0a1b2c3d4e/);
  await select.focus();
  await expect(select).toBeFocused();
  const displayed = page.locator('.account-selector-value');
  await expect(displayed).toHaveText(`${LONG_DISPLAY_NAME} — Отключено`);
  const dimensions = await displayed.evaluate((element) => ({
    scrollWidth: element.scrollWidth,
    clientWidth: element.clientWidth,
    outline: getComputedStyle(element.previousElementSibling!).outlineStyle,
  }));
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth);
  expect(dimensions.outline).not.toBe('none');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('пользовательские страницы не выходят за экран при увеличении текста', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  for (const [path, heading] of [
    ['/app/settings/connections', 'Подключения'],
    [`/app?account=${MAIN_ID}`, 'Обзор'],
    ['/app/positions/btc', 'BTCUSDT'],
  ]) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1, name: new RegExp(`^${heading}`) })).toBeVisible();
    await page.addStyleTag({ content: 'html { font-size: 200% !important; }' });
    expect(await page.evaluate(() => document.documentElement.scrollWidth), path).toBeLessThanOrEqual(320);
  }
});

test('интервалы текста не вызывают горизонтальное переполнение', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  for (const [path, heading] of [
    ['/app/settings/connections', 'Подключения'],
    [`/app?account=${MAIN_ID}`, 'Обзор'],
    ['/app/positions/btc', 'BTCUSDT'],
  ]) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1, name: new RegExp(`^${heading}`) })).toBeVisible();
    await page.addStyleTag({ content: `
      * { line-height: 1.5 !important; letter-spacing: 0.12em !important; word-spacing: 0.16em !important; }
      p { margin-bottom: 2em !important; }
    ` });
    expect(await page.evaluate(() => document.documentElement.scrollWidth), path).toBeLessThanOrEqual(320);
  }
});

test('таблица свечей доступна с клавиатуры при ширине 320 CSS px', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  await page.goto('/app/positions/btc');
  const disclosure = page.getByText('Данные графика и ценовые уровни');
  await disclosure.focus();
  await page.keyboard.press('Enter');
  const region = page.getByRole('region', { name: 'Таблица свечей' });
  await expect(region).toBeVisible();
  await page.keyboard.press('Tab');
  await expect(region).toBeFocused();
  const dimensions = await region.evaluate((element) => ({
    width: element.clientWidth,
    scrollWidth: element.scrollWidth,
    start: element.scrollLeft,
  }));
  expect(dimensions.scrollWidth).toBeGreaterThan(dimensions.width);
  await page.keyboard.press('ArrowRight');
  await expect.poll(() => region.evaluate((element) => element.scrollLeft)).toBeGreaterThan(dimensions.start);
  for (let index = 0; index < 60; index += 1) await page.keyboard.press('ArrowRight');
  await expect.poll(() => region.evaluate((element) =>
    element.scrollLeft + element.clientWidth >= element.scrollWidth - 1,
  )).toBe(true);
  for (let index = 0; index < 60; index += 1) await page.keyboard.press('ArrowLeft');
  await expect.poll(() => region.evaluate((element) => element.scrollLeft)).toBe(0);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
  await expect(page.getByRole('table', { name: 'Свечи BTCUSDT, 1h, UTC' })).toBeVisible();
});

test('reflow заполненных страниц и раскрытых данных графика на 320 CSS px', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'chromium', 'Визуальный reflow проверяется в Chromium');
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);
  for (const [path, heading, imageName] of [
    ['/app/settings/connections', 'Подключения', 'connections.png'],
    [`/app?account=${MAIN_ID}`, 'Обзор', 'overview.png'],
    ['/app/positions/btc', 'BTCUSDT', 'position.png'],
  ]) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1, name: new RegExp(`^${heading}`) })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth), path).toBeLessThanOrEqual(320);
    await page.screenshot({ path: testInfo.outputPath(imageName), fullPage: true });
  }
  const disclosure = page.getByText('Данные графика и ценовые уровни');
  await disclosure.click();
  const region = page.getByRole('region', { name: 'Таблица свечей' });
  await expect(region).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
  await region.scrollIntoViewIfNeeded();
  await page.screenshot({ path: testInfo.outputPath('chart-table.png') });
});

test('негативные состояния: axe и reflow загрузки, пустого списка, 503 и 404 на 320 CSS px', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await loginThroughLanding(page);
  const connections = '/app/settings/connections';
  let accountsState: 'loading' | 'empty' | 'error' = 'loading';
  await page.route('**/bff/me/exchange-accounts', async (route) => {
    if (accountsState === 'loading') {
      // Переход на следующий экран отменит незавершённый запрос.
      return;
    }
    if (accountsState === 'empty') {
      await route.fulfill({ json: { items: [] } });
    } else {
      await route.fulfill({ status: 503, json: {} });
    }
  });
  await page.route('**/bff/me/positions/missing', (route) => route.fulfill({
    status: 404,
    json: { code: 'resource_not_found' },
  }));

  async function expectAccessibleState(label: string) {
    const result = await new AxeBuilder({ page })
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
      .analyze();
    expect(result.violations.map(({ id }) => id), label).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth), label).toBeLessThanOrEqual(320);
  }

  await page.goto(connections, { waitUntil: 'domcontentloaded' });
  await expect(page.getByText('Загружаем подключения…')).toBeVisible();
  await expectAccessibleState('Загрузка подключений');

  accountsState = 'empty';
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(page.getByText(/Подключений пока нет/)).toBeVisible();
  await expectAccessibleState('Пустой список подключений');

  accountsState = 'error';
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('alert')).toContainText('Сервис временно недоступен');
  await expectAccessibleState('Ошибка подключений 503');

  await page.goto('/app/positions/missing');
  await expect(page.getByRole('heading', { level: 1, name: 'Позиция недоступна' })).toBeVisible();
  await expectAccessibleState('Позиция не найдена');
});
