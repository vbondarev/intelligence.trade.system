import { expect, test, type Page, type Request, type Route } from '@playwright/test';
import { identityBaseUrl, loginThroughLanding, webBaseUrl } from './support';
import { MAIN_ID, mockBusinessData, mockPositionPage } from './uiMock';

// Frontend service — единственный browser-facing origin: React routes обслуживает он сам,
// а /bff/** и OIDC callbacks проксирует во внутренний BFF.
test('frontend service serves React routes and proxies the BFF boundary on the same origin', async ({ request }) => {
  for (const path of [
    '/',
    '/app',
    '/app/settings/connections',
    '/app/positions/8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001',
    '/app/nested/route',
  ]) {
    const page = await request.get(path);
    expect(page.status(), path).toBe(200);
    expect(page.headers()['content-type']).toContain('text/html');
    expect(await page.text()).toContain('<div id="root">');
  }

  const session = await request.get('/bff/auth/session');
  expect(session.status()).toBe(200);
  expect(session.headers()['content-type']).toContain('application/json');
  expect(session.headers()['cache-control']).toContain('no-store');
  expect(await session.json()).toEqual({ authenticated: false });

  const unknownBffRoute = await request.get('/bff/unknown');
  expect(unknownBffRoute.status()).toBe(404);
  expect(await unknownBffRoute.text()).not.toContain('<div id="root">');

  // Явные endpoints подключений обслуживает BFF, а не SPA fallback: без session список недоступен,
  // а незапланированные пути под тем же prefix остаются 404.
  const anonymousConnections = await request.get('/bff/me/exchange-accounts');
  expect(anonymousConnections.status()).toBe(401);
  expect(anonymousConnections.headers()['cache-control']).toContain('no-store');
  expect(await anonymousConnections.text()).not.toContain('<div id="root">');

  for (const path of [
    '/bff/me/positions/8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001',
    '/bff/me/positions/8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001/candles?interval=1h&limit=200',
    '/bff/me/positions/8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001/evaluation',
  ]) {
    const anonymousPosition = await request.get(path);
    expect(anonymousPosition.status(), path).toBe(401);
    expect(await anonymousPosition.text(), path).not.toContain('<div id="root">');
  }

  for (const path of [
    '/bff/me/exchange-accounts/not-a-guid',
    '/bff/me/exchange-accounts/2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6/sync',
    '/bff/me/positions/not-a-guid',
    '/bff/me/positions/8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001/sync',
  ]) {
    const unplanned = await request.get(path);
    expect(unplanned.status(), path).toBe(404);
    expect(await unplanned.text(), path).not.toContain('<div id="root">');
  }

  const login = await request.get('/bff/auth/login?returnUrl=/app', { maxRedirects: 0 });
  expect(login.status()).toBe(302);
  const preparation = new URL(login.headers()['location']);
  expect(preparation.origin).toBe(identityBaseUrl);
  expect(preparation.pathname).toBe('/account/web-login');
  expect(login.headers()['set-cookie']).toBeUndefined();
  const continuation = await request.get('/bff/auth/login/continue?returnUrl=/app', { maxRedirects: 0 });
  expect(continuation.status()).toBe(302);
  const authorize = new URL(continuation.headers()['location']);
  expect(authorize.origin).toBe(identityBaseUrl);
  expect(authorize.searchParams.get('prompt')).toBe('none');
  expect(authorize.searchParams.get('redirect_uri')).toBe(`${webBaseUrl}/signin-oidc`);
});

const webOrigin = new URL(webBaseUrl).origin;
const positionPath = '/app/positions/btc';
const positionUrlWithChart = positionPath + '?interval=4h#chart';
const loadingText = 'Загружаем страницу позиции…';

function isFrontendScript(url: URL) {
  return url.origin === webOrigin && url.pathname.startsWith('/assets/') && url.pathname.endsWith('.js');
}

async function openPositionOverview(page: Page) {
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);

  const initialScripts = new Set<string>();
  const positionReads: string[] = [];
  const recordInitialScript = (request: Request) => {
    if (isFrontendScript(new URL(request.url()))) initialScripts.add(request.url());
  };
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.origin === webOrigin && /^\/bff\/me\/positions\/btc(?:\/|$)/.test(url.pathname)) {
      positionReads.push(url.pathname);
    }
  });
  page.on('request', recordInitialScript);
  await page.goto('/app');
  const positionLink = page.getByRole('link', { name: /^BTCUSDT/ });
  await expect(page.getByRole('heading', { name: 'Обзор', exact: true })).toBeVisible();
  await expect(positionLink).toBeVisible();
  page.off('request', recordInitialScript);
  const entryScript = await page.locator('script[type="module"][src]').evaluate((script) => (script as HTMLScriptElement).src);
  expect(initialScripts.has(entryScript)).toBe(true);

  return {
    positionLink,
    entryScript,
    positionReads,
    isNewScript: (url: URL) => isFrontendScript(url) && !initialScripts.has(url.href),
  };
}

test('прямой URL позиции загружает график и сохраняет интервал из адреса', async ({ page }) => {
  await mockBusinessData(page);
  await mockPositionPage(page);
  await loginThroughLanding(page);

  await page.goto(positionUrlWithChart);
  await expect(page.getByRole('heading', { name: /^BTCUSDT/, level: 1 })).toBeVisible();
  await expect(page.getByRole('group', { name: /Свечной график BTCUSDT, интервал 4h/ }).locator('canvas').first()).toBeVisible();
  await expect(page.getByRole('button', { name: '4h' })).toHaveAttribute('aria-pressed', 'true');
  await expect(page.getByRole('link', { name: '← К списку позиций' })).toHaveAttribute('href', '/app?account=' + MAIN_ID);
});

const delayedPositionScenarios = {
  loaded: 'после загрузки модуля фокус переходит на заголовок позиции',
  keyboard: 'после перехода к позиции клавишей Enter фокус получает заголовок',
  'keep-focus': 'завершение загрузки сохраняет фокус, перенесённый пользователем',
  'skip-link': 'переход к основному содержимому сохраняет фокус после загрузки модуля',
  'already-on-main': 'завершение загрузки сохраняет фокус на основном содержимом, если оно уже было активно',
  leave: 'после ухода с маршрута завершение загрузки не открывает позицию',
  expired: 'после истечения сессии завершение загрузки не открывает позицию',
} as const;

for (const [outcome, title] of Object.entries(delayedPositionScenarios)) {
  test(title, async ({ page, browserName }) => {
    const windowsWebKit = process.platform === 'win32' && browserName === 'webkit';
    const preservesMainFocus = outcome === 'skip-link' || outcome === 'already-on-main';
    test.skip(preservesMainFocus && !windowsWebKit, 'Проверка происхождения фокуса на main в Windows WebKit');
    test.skip(outcome === 'keyboard' && windowsWebKit, 'Клавиатурный сценарий A1 проверяется в Chromium и Linux WebKit');
    const { positionLink, positionReads, isNewScript } = await openPositionOverview(page);
    const main = page.getByRole('main');
    const skipLink = page.getByRole('link', { name: 'Перейти к основному содержимому' });
    const requested = new Set<string>();
    let release!: () => void;
    const held = new Promise<void>((resolve) => { release = resolve; });
    const pauseRouteModule = async (route: Route) => {
      requested.add(route.request().url());
      await held;
      await route.continue();
    };
    await page.route(isNewScript, pauseRouteModule);
    try {
      if (outcome === 'already-on-main') {
        await skipLink.focus();
        await page.keyboard.press('Enter');
        await expect(main).toBeFocused();
      }

      if (outcome === 'keyboard') {
        await positionLink.focus();
        await page.keyboard.press('Enter');
      } else {
        await positionLink.click();
      }
      const navigation = page.getByRole('navigation', { name: 'Основная навигация' });
      const loading = main.getByRole('status').filter({ hasText: loadingText });
      await expect(loading).toBeVisible();
      await expect(navigation).toBeVisible();
      await expect(page).toHaveTitle('Позиция — Intelligence Trade');
      await expect.poll(() => requested.size).toBeGreaterThan(0);
      expect(positionReads).toHaveLength(0);
      if (windowsWebKit && (outcome === 'loaded' || preservesMainFocus)) await expect(main).toBeFocused();

      const searchLink = navigation.getByRole('link', { name: 'Поиск позиций' });
      if (outcome === 'keep-focus') await searchLink.focus();
      if (outcome === 'skip-link') {
        await skipLink.focus();
        await page.keyboard.press('Enter');
        await expect(main).toBeFocused();
      }
      if (outcome === 'leave') await navigation.getByRole('link', { name: 'Обзор' }).click();

      const loginAgain = page.getByRole('dialog', { name: 'Сессия завершена' })
        .getByRole('button', { name: 'Войти снова' });
      if (outcome === 'expired') {
        await page.route('**/bff/auth/session/timing', (route) => route.fulfill({ status: 401 }));
        await page.evaluate(() => window.dispatchEvent(new Event('pageshow')));
        await expect(loginAgain).toBeFocused();
      }

      const moduleResponse = page.waitForResponse((response) => requested.has(response.url()) && response.ok());
      release();
      await (await moduleResponse).finished();
      await expect(loading).toHaveCount(0);

      const positionHeading = main.getByRole('heading', { name: /^BTCUSDT/, level: 1 });
      if (outcome === 'expired' || outcome === 'leave') {
        await expect(positionHeading).toHaveCount(0);
        expect(positionReads).toHaveLength(0);
        if (outcome === 'expired') await expect(loginAgain).toBeFocused();
        else await expect(main.getByRole('heading', { name: 'Обзор' })).toBeFocused();
        return;
      }

      await expect(positionHeading).toBeVisible();
      await expect(main.getByRole('group', { name: /^Свечной график BTCUSDT/ }).locator('canvas').first()).toBeVisible();
      await expect(page).toHaveTitle(/^BTCUSDT .+ — Intelligence Trade$/);
      const expectedFocus = preservesMainFocus ? main : outcome === 'keep-focus' ? searchLink : positionHeading;
      await expect(expectedFocus).toBeFocused();
    } finally {
      release();
      await page.unroute(isNewScript, pauseRouteModule);
    }
  });
}

for (const keepMissing of [true, false]) {
  test(keepMissing ? 'после обновления отсутствующий модуль снова показывает ошибку' : 'обновление восстанавливает доступный модуль позиции', async ({ page }) => {
    const { entryScript, isNewScript, positionLink, positionReads } = await openPositionOverview(page);
    const failedScripts = new Set<string>();
    const rejectNewScripts = (route: Route) => {
      failedScripts.add(route.request().url());
      return route.fulfill({ status: 404 });
    };
    await page.route(isNewScript, rejectNewScripts);
    await positionLink.click();

    const main = page.getByRole('main');
    const alert = main.getByRole('alert').filter({ hasText: 'Обновите страницу, чтобы повторить загрузку.' });
    const navigation = page.getByRole('navigation', { name: 'Основная навигация' });
    await expect(alert).toBeVisible();
    await expect(navigation).toBeVisible();
    expect(positionReads).toHaveLength(0);
    expect(failedScripts.size).toBeGreaterThan(0);
    await page.unroute(isNewScript, rejectNewScripts);

    // Первоначальный переход был клиентским. Query и hash включаем в проверку обновления того же документа.
    await page.evaluate((url) => window.history.replaceState(window.history.state, '', url), positionUrlWithChart);
    if (keepMissing) {
      const missingModuleUrls = new Set(failedScripts);
      // После reload отклоняем только уже обнаруженные модули, чтобы entry и его зависимости загрузились.
      await page.route((url) => missingModuleUrls.has(url.href), (route) => route.fulfill({ status: 404 }));
    }

    const documentRequests: string[] = [];
    page.on('request', (request) => {
      if (request.isNavigationRequest() && request.frame() === page.mainFrame()) documentRequests.push(request.url());
    });
    const entryResponse = page.waitForResponse((response) => response.url() === entryScript && response.ok());
    const failedAgain = keepMissing
      ? page.waitForResponse((response) => failedScripts.has(response.url()) && response.status() === 404)
      : undefined;
    const reload = main.getByRole('button', { name: 'Обновить страницу' });
    await reload.focus();
    await expect(reload).toBeFocused();
    await page.keyboard.press('Enter');
    await entryResponse;
    await expect(page).toHaveURL(new URL(positionUrlWithChart, webOrigin).href);

    if (failedAgain) {
      await failedAgain;
      await expect(alert).toBeVisible();
      await expect(navigation).toBeVisible();
      expect(positionReads).toHaveLength(0);

      const back = main.getByRole('link', { name: 'Вернуться к списку' });
      await back.focus();
      await expect(back).toBeFocused();
      await page.keyboard.press('Enter');
      await expect(page.getByRole('heading', { name: 'Обзор' })).toBeVisible();
      expect(documentRequests).toHaveLength(1);
    } else {
      await expect(alert).toHaveCount(0);
      await expect(main.getByRole('heading', { name: /^BTCUSDT/, level: 1 })).toBeVisible();
      await expect(main.getByRole('group', { name: /Свечной график BTCUSDT, интервал 4h/ }).locator('canvas').first()).toBeVisible();
      expect(documentRequests).toHaveLength(1);
    }
  });
}
