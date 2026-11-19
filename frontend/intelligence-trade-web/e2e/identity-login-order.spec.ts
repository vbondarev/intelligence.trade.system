import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import {
  IDENTITY_COOKIE, SESSION_COOKIE, identityBaseUrl, openIdentityLoginThroughLanding, submitIdentityLogin, webBaseUrl,
} from './support';

const challengeCookie = /^\.AspNetCore\.(Correlation\.|OpenIdConnect\.Nonce\.)/;
const connectionsPath = '/app/settings/connections';

async function authenticateAndPauseBeforeBff(page: Page) {
  let identityContinuation = '';
  const loginUrl = identityBaseUrl + '/account/login';
  // Playwright маршрутизирует начало цепочки. Останавливаемся после реального POST
  // и выполняем каждый последующий переход явно, чтобы вмешаться между шагами.
  await page.route(loginUrl, async route => {
    const response = await route.fetch({ maxRedirects: 0 });
    expect(response.status()).toBe(303);
    identityContinuation = new URL(response.headers().location, identityBaseUrl).href;
    await route.fulfill({ response, status: 200, contentType: 'text/html', body: '<title>Login checkpoint</title>' });
  }, { times: 1 });
  await submitIdentityLogin(page);
  await expect(page).toHaveTitle('Login checkpoint');
  const continuation = await page.request.get(identityContinuation, { maxRedirects: 0 });
  expect(continuation.status()).toBe(302);
  const bffContinuation = new URL(continuation.headers().location);
  expect(bffContinuation.origin).toBe(webBaseUrl);
  expect(bffContinuation.pathname).toBe('/bff/auth/login/continue');
  return bffContinuation;
}

test('форма на прямой ссылке предшествует единственному OIDC challenge и сохраняет назначение', async ({ page, context }) => {
  let prepares = 0;
  let authorizations = 0;
  page.on('request', request => {
    const url = new URL(request.url());
    if (url.pathname === '/account/web-login') prepares += 1;
    if (url.pathname === '/connect/authorize') authorizations += 1;
  });
  await page.goto(connectionsPath);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await page.waitForURL(url => url.origin === identityBaseUrl && url.pathname === '/account/login');
  expect(prepares).toBe(1);
  expect(authorizations).toBe(0);
  expect((await context.cookies(webBaseUrl + '/signin-oidc')).some(cookie => challengeCookie.test(cookie.name))).toBe(false);

  const posted = page.waitForResponse(response =>
    response.url() === identityBaseUrl + '/account/login' && response.request().method() === 'POST');
  const authorize = page.waitForRequest(request => new URL(request.url()).pathname === '/connect/authorize');
  await submitIdentityLogin(page);
  expect((await posted).status()).toBe(303);
  const query = new URL((await authorize).url()).searchParams;
  expect(query.get('prompt')).toBe('none');
  expect(query.get('code_challenge_method')).toBe('S256');
  await expect(page).toHaveURL(webBaseUrl + connectionsPath);
  await expect(page.getByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
  expect(authorizations).toBe(1);

  // С действующей Identity SSO новый вход завершается без повторной формы.
  await context.clearCookies({ name: SESSION_COOKIE });
  let extraForms = 0;
  page.on('request', request => {
    if (new URL(request.url()).pathname === '/account/login') extraForms += 1;
  });
  await page.goto('/bff/auth/login?returnUrl=/app');
  await expect(page.getByRole('heading', { name: 'Обзор', level: 1 })).toBeVisible();
  expect(extraForms).toBe(0);
});

test('потеря Identity SSO между шагами даёт доступное сообщение и явный повтор', async ({ page, context }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await page.goto(connectionsPath);
  await page.getByRole('button', { name: 'Войти', exact: true }).click();
  await page.waitForURL(url => url.origin === identityBaseUrl && url.pathname === '/account/login');

  const continuation = await authenticateAndPauseBeforeBff(page);
  let continuations = 0;
  page.on('request', request => {
    if (new URL(request.url()).pathname !== '/bff/auth/login/continue') return;
    continuations += 1;
    expect(request.method()).toBe('GET');
    expect(request.postData()).toBeNull();
  });
  await context.clearCookies({ name: IDENTITY_COOKIE });
  await page.goto(continuation.href);
  await expect(page.getByRole('alert')).toHaveText('Не удалось завершить вход. Начните вход заново.');
  const retry = page.getByRole('button', { name: 'Начать вход заново' });
  await expect(retry).toBeFocused();
  expect(continuations).toBe(1);
  expect((await page.request.get('/bff/auth/session/timing')).status()).toBe(401);
  const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
  expect(result.violations.map(({ id }) => id)).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);
  await page.keyboard.press('Enter');
  await page.waitForURL(url => url.origin === identityBaseUrl && url.pathname === '/account/login');
  await submitIdentityLogin(page);
  await expect(page).toHaveURL(webBaseUrl + connectionsPath);
  expect(continuations).toBe(2);
});

test('старый callback и подмена локального назначения отклоняются', async ({ page, context }) => {
  for (const path of ['/account/web-login', '/account/web-login/continue']) {
    const result = await page.request.get(identityBaseUrl + path + '?returnUrl=%2F%2Funtrusted.example', { maxRedirects: 0 });
    expect(result.status()).toBe(400);
  }
  const csrf = await page.request.post(identityBaseUrl + '/account/login', { form: { username: 'test' }, maxRedirects: 0 });
  expect(csrf.status()).toBe(400);

  await openIdentityLoginThroughLanding(page);
  const callback = page.waitForRequest(request => new URL(request.url()).pathname === '/signin-oidc');
  await submitIdentityLogin(page);
  await expect(page.getByRole('heading', { name: 'Обзор', level: 1 })).toBeVisible();
  const previous = await callback;
  await context.clearCookies({ name: SESSION_COOKIE });
  const replay = await page.request.fetch(previous.url(), {
    method: previous.method(),
    data: previous.postData() ?? undefined,
    headers: previous.method() === 'POST' ? { 'Content-Type': 'application/x-www-form-urlencoded' } : undefined,
    maxRedirects: 0,
  });
  expect(replay.status()).toBe(302);
  expect((await page.request.get('/bff/auth/session/timing')).status()).toBe(401);
});

for (const parameter of ['state', 'nonce', 'code_challenge'] as const) {
  test(`подмена ${parameter} в запросе OIDC отклоняется`, async ({ page }) => {
    await openIdentityLoginThroughLanding(page);
    const continuation = await authenticateAndPauseBeforeBff(page);
    const challenge = await page.request.get(continuation.href, { maxRedirects: 0 });
    expect(challenge.status()).toBe(302);
    const authorization = new URL(challenge.headers().location);
    expect(authorization.origin).toBe(identityBaseUrl);
    expect(authorization.pathname).toBe('/connect/authorize');
    authorization.searchParams.set(parameter, 'x'.repeat(43));
    await page.goto(authorization.href);
    await expect(page.getByRole('alert')).toHaveText('Не удалось завершить вход. Начните вход заново.');
    expect((await page.request.get('/bff/auth/session/timing')).status()).toBe(401);
  });
}

test('две вкладки сохраняют разные локальные назначения и чистый контекст не получает сессию', async ({ browser, page, context }) => {
  await openIdentityLoginThroughLanding(page);
  const second = await context.newPage();
  await second.goto(connectionsPath);
  await second.getByRole('button', { name: 'Войти', exact: true }).click();
  await second.waitForURL(url => url.origin === identityBaseUrl && url.pathname === '/account/login');

  await submitIdentityLogin(page);
  await expect(page).toHaveURL(webBaseUrl + '/app');
  // Первая вкладка сменила Identity principal. Форма второй обновляет
  // привязанный к principal antiforgery token, сохраняя своё назначение.
  await second.reload();
  await submitIdentityLogin(second);
  await expect(second).toHaveURL(webBaseUrl + connectionsPath);

  const independent = await browser.newContext();
  try {
    expect((await independent.request.get(webBaseUrl + '/bff/auth/session/timing')).status()).toBe(401);
  } finally {
    await independent.close();
  }
});
