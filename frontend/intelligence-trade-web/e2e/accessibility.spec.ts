import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { firstTimelinePage } from '../src/test/positionFixtures';
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

test('название и ссылки навигации переносятся при широких метриках текста', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await mockBusinessData(page);
  await loginThroughLanding(page);
  await page.goto('/app/settings/connections');
  await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
  await page.addStyleTag({ content: `
    html { font-size: 200% !important; }
    .brand, .side-nav-link { letter-spacing: .12em; }
  ` });

  const brand = page.locator('.brand');
  const navigation = page.getByRole('navigation', { name: 'Основная навигация' });
  await expect(brand).toHaveText('Intelligence Trade');
  for (const name of ['Обзор', 'Подключения', 'Поиск позиций']) {
    await expect(navigation.getByRole('link', { name, exact: true })).toBeVisible();
  }
  const layout = await page.evaluate(async () => {
    await document.fonts.ready;
    return {
      viewportWidth: window.innerWidth,
      documentWidth: document.documentElement.scrollWidth,
      labels: Array.from(document.querySelectorAll('.brand, .side-nav-link'), (element) => {
        const { left, right, width } = element.getBoundingClientRect();
        return { text: element.textContent, left, right, width, scrollWidth: element.scrollWidth, clientWidth: element.clientWidth };
      }),
    };
  });
  expect(layout.viewportWidth).toBe(320);
  expect(layout.documentWidth).toBeLessThanOrEqual(layout.viewportWidth);
  expect(layout.labels).toHaveLength(4);
  for (const label of layout.labels) {
    const name = label.text ?? '';
    expect(label.left, name).toBeGreaterThanOrEqual(0);
    expect(label.right, name).toBeLessThanOrEqual(layout.viewportWidth);
    expect(label.width, name).toBeGreaterThan(0);
    expect(label.scrollWidth, name).toBeLessThanOrEqual(label.clientWidth);
  }

  const overview = navigation.getByRole('link', { name: 'Обзор', exact: true });
  await overview.focus();
  await expect(overview).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.getByRole('heading', { name: 'Обзор', level: 1 })).toBeVisible();
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

for (const reducedMotion of ['no-preference', 'reduce'] as const) {
  test(`индикатор длительной загрузки истории сохраняет статус (${reducedMotion})`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion });
    await mockBusinessData(page);
    await mockPositionPage(page);
    await loginThroughLanding(page);
    await page.goto('/app/positions/btc');

    const history = page.getByRole('region', { name: 'История позиции' });
    await expect(history).toBeVisible();
    // Стартовый realtime recovery может заменить первый GET открытой истории.
    await expect(page.getByText(/^Realtime:/)).toBeHidden();

    const visibleStatus = history.locator('.inline-status');
    const spinner = visibleStatus.locator('.spinner');
    const events = history.getByRole('list', { name: 'События истории позиции' });
    const motionEnabled = reducedMotion === 'no-preference';

    const holdResponse = async (status: number) => {
      let release!: () => void;
      const responseGate = new Promise<void>((resolve) => { release = resolve; });
      await page.route('**/bff/me/positions/btc/timeline?*', async (route) => {
        await responseGate;
        await route.fulfill({ status, json: status === 200 ? firstTimelinePage() : {} });
      }, { times: 1 });
      return release;
    };

    const expectLoading = async (message: string, continuous: boolean) => {
      await expect(visibleStatus).toBeVisible();
      await expect(visibleStatus).toHaveText(message);
      await expect(history.getByRole('status')).toHaveText(message);
      await expect(spinner).toHaveCSS('animation-name', motionEnabled ? 'spin' : 'none');
      if (motionEnabled) {
        await expect(spinner).toHaveCSS('animation-duration', '0.8s');
        await expect(spinner).toHaveCSS('animation-iteration-count', continuous ? 'infinite' : '6');
      }
      const running = await spinner.evaluate(async (element) => {
        // Ответ удерживается, чтобы проверить состояние после лимита анимации.
        await new Promise((resolve) => setTimeout(resolve, 5100));
        return element.getAnimations().some((animation) => animation.playState === 'running');
      });
      expect(running).toBe(motionEnabled && continuous);
      await expect(visibleStatus).toHaveText(message);
      await expect(visibleStatus).toBeVisible();
    };

    const releaseInitial = await holdResponse(200);
    try {
      await history.getByRole('button', { name: 'История позиции' }).click();
      await expectLoading('Загружаем историю…', false);
    } finally {
      releaseInitial();
    }
    await expect(events).toBeVisible();
    await expect(visibleStatus).toBeHidden();

    const releaseRefresh = await holdResponse(503);
    try {
      await history.getByRole('button', { name: 'Обновить', exact: true }).press('Enter');
      await expectLoading('Обновляем историю…', true);
    } finally {
      releaseRefresh();
    }
    await expect(history.getByRole('alert')).toContainText('Сервис временно недоступен');
    await expect(visibleStatus).toBeHidden();

    const releaseRetry = await holdResponse(200);
    try {
      await history.getByRole('button', { name: 'Повторить', exact: true }).press('Enter');
      await expect(visibleStatus).toHaveText('Обновляем историю…');
      await expect(spinner).toHaveCSS('animation-name', motionEnabled ? 'spin' : 'none');
      if (motionEnabled) await expect(spinner).toHaveCSS('animation-iteration-count', 'infinite');
    } finally {
      releaseRetry();
    }
    await expect(visibleStatus).toBeHidden();
    await expect(events).toBeVisible();
    await expect(history.getByRole('alert')).toBeHidden();
  });
}
