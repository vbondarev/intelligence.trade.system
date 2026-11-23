import { expect, test, type Locator, type Page } from '@playwright/test';
import { timelineEvaluationItem, timelinePageFixture, timelineRecommendationItem } from '../src/test/positionFixtures';
import { LONG_DISPLAY_NAME, MAIN_ID, mockBusinessData, mockPositionPage } from './uiMock';
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

for (const viewport of viewports) {
  test(`shell is usable on ${viewport.name} ${viewport.width}x${viewport.height}`, async ({ page }) => {
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
    await expect(page.getByText('REST-снимок устарел')).toBeVisible();

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
    await expect(page).toHaveURL(/side=short/);
    await expect(page).not.toHaveURL(/[?&]state=/);
    await expect(page.getByRole('article', { name: /^ADAUSDT/ })).toBeVisible();
    await expect(page.getByRole('article', { name: /^XRPUSDT/ })).toHaveCount(0);
    await expect(page.getByRole('article', { name: /^SOLUSDT/ })).toHaveCount(0);
    await expect(page.getByLabel('Состояние')).toHaveCount(0);
    await expectNoHorizontalScroll(page);

    await page.getByLabel('Символ').fill('ADAUSDT');
    await page.getByRole('button', { name: 'Найти' }).click();
    await expect(page).toHaveURL(/symbol=ADAUSDT/);
    await expect(page.getByRole('article', { name: /^ADAUSDT/ })).toBeVisible();
    await expectNoHorizontalScroll(page);

    await page.goto('/app/settings/connections');
    await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
    await expectNoHorizontalScroll(page);
  });
}

for (const viewport of viewports) {
  test(`position page is usable on ${viewport.name} ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await mockBusinessData(page);
    const timelineRequests = await mockPositionPage(page);

    await loginThroughLanding(page);
    await page.goto(`/app?account=${MAIN_ID}&side=long`);
    await page.getByRole('link', { name: /^BTCUSDT/ }).click();
    await expect(page).toHaveURL(/\/app\/positions\/btc$/);
    await expect(page.getByRole('heading', { level: 1, name: /^BTCUSDT/ })).toBeVisible();
    await expect(page.getByRole('heading', { level: 1, name: /^BTCUSDT/ })).toBeFocused();

    const chart = page.getByRole('group', { name: /^Свечной график BTCUSDT/ });
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
    await expect(page).toHaveURL(/interval=4h/);
    await expect(page.getByRole('group', { name: /интервал 4h$/ })).toBeVisible();
    await expect(page.getByRole('table', { name: /Свечи BTCUSDT, 4h, UTC/ }).getByRole('row')).toHaveCount(201);
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

    await page.getByRole('link', { name: '← К списку позиций' }).click();
    await expect(page).toHaveURL(new RegExp(`/app\\?account=${MAIN_ID}&side=long$`));
    await expect(page.getByRole('article', { name: /^BTCUSDT/ })).toBeVisible();
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
