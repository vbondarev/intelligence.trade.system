import AxeBuilder from '@axe-core/playwright';
import { expect, test } from '@playwright/test';
import { IDENTITY_COOKIE, loginThroughLanding, SESSION_COOKIE } from './support';

const timingPath = '**/bff/auth/session/timing';
const serverTime = Date.UTC(2026, 10, 17);
const timing = (remaining: number) => ({
  serverTimeUtc: new Date(serverTime).toISOString(),
  expiresAtUtc: new Date(serverTime + remaining).toISOString(),
});

test('предупреждение при 320 CSS px сохраняет форму, фокус и синхронизирует две вкладки', async ({ page, context }) => {
  await page.setViewportSize({ width: 320, height: 740 });
  await page.clock.setSystemTime(new Date('2040-01-01'));
  await loginThroughLanding(page);
  const other = await context.newPage();
  await other.goto('/app');
  let warned = false;
  let extended = false;

  // В первой вкладке ускоряем только приближение серверного срока. POST, cookie и CSRF настоящие.
  for (const tab of [page, other]) {
    await tab.route(timingPath, (route) => extended
      ? route.continue()
      : route.fulfill({ json: timing(warned ? 115_000 : 300_000) }));
  }

  await page.goto('/app/settings/connections');
  const name = page.getByLabel('Название *', { exact: true });
  await name.fill('Черновик подключения');
  await name.focus();
  // Замедляем существующий переход, чтобы проверка первого фокуса не зависела от скорости браузера.
  await page.addStyleTag({ content: '.mantine-Modal-content { transition-duration: 1s !important; }' });
  await page.evaluate(() => {
    document.addEventListener('focusin', (event) => {
      const target = event.target;
      if (!(target instanceof HTMLButtonElement) || target.textContent?.trim() !== 'Продлить сессию') return;
      const dialog = target.closest('[role="dialog"]');
      if (dialog && !dialog.hasAttribute('data-opacity-at-first-focus')) {
        dialog.setAttribute('data-opacity-at-first-focus', getComputedStyle(dialog).opacity);
      }
    });
  });
  warned = true;
  for (const tab of [other, page]) {
    await tab.evaluate(() => window.dispatchEvent(new Event('focus')));
    await expect(tab.getByRole('dialog', { name: 'Сессия скоро завершится' })).toBeVisible();
  }

  const dialog = page.getByRole('dialog', { name: 'Сессия скоро завершится' });
  const extend = dialog.getByRole('button', { name: 'Продлить сессию' });
  await expect(extend).toBeFocused();
  await expect(dialog).toHaveAttribute('data-opacity-at-first-focus', '1');
  await expect(dialog).toHaveAccessibleDescription('Продлите сессию, чтобы сохранить доступ к приложению.');
  const audit = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .analyze();
  expect(audit.violations.map(({ id, nodes }) => ({
    id,
    nodes: nodes.map(({ target, failureSummary }) => ({ target, failureSummary })),
  }))).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(320);

  extended = true;
  const renewed = page.waitForResponse((response) => response.url().endsWith('/bff/auth/session/extend'));
  await page.keyboard.press('Enter');
  const response = await renewed;
  expect(response.ok()).toBe(true);
  expect(Object.keys(await response.json()).sort()).toEqual(['expiresAtUtc', 'serverTimeUtc']);
  await expect(dialog).toBeHidden();
  await expect(name).toBeFocused();
  await expect(name).toHaveValue('Черновик подключения');
  await expect(other.getByRole('dialog')).toBeHidden();
});

test('сбой продления оставляет предупреждение, после возврата учитывается истечение', async ({ page }) => {
  await loginThroughLanding(page);
  let expired = false;
  await page.route(timingPath, (route) => expired
    ? route.fulfill({ status: 401 })
    : route.fulfill({ json: timing(100_000) }));
  await page.route('**/bff/auth/session/extend', (route) => route.abort('failed'));
  await page.reload();
  const warning = page.getByRole('dialog', { name: 'Сессия скоро завершится' });
  await warning.getByRole('button', { name: 'Продлить сессию' }).click();
  await expect(warning.getByRole('alert')).toContainText('Не удалось подтвердить продление');
  await expect(warning).toBeVisible();
  await warning.getByRole('button', { name: 'Продолжить без продления' }).click();
  await page.getByRole('button', { name: 'Приостановить автообновление' }).click();
  await expect(page.getByRole('button', { name: 'Возобновить автообновление' })).toBeVisible();

  expired = true;
  await page.evaluate(() => window.dispatchEvent(new Event('pageshow')));
  const ended = page.getByRole('dialog', { name: 'Сессия завершена' });
  await expect(ended).toBeVisible();
  await expect(ended.getByRole('button', { name: 'Войти снова' })).toBeFocused();
  await expect(ended.getByRole('button', { name: 'Продлить сессию' })).toHaveCount(0);
  await expect(page.getByRole('heading', { name: 'Обзор' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Возобновить автообновление' })).toHaveCount(0);
});

test('истечение в открытом предупреждении переносит фокус на повторный вход', async ({ page }) => {
  await loginThroughLanding(page);
  let expired = false;
  await page.route(timingPath, (route) => expired
    ? route.fulfill({ status: 401 })
    : route.fulfill({ json: timing(100_000) }));
  await page.reload();
  const warning = page.getByRole('dialog', { name: 'Сессия скоро завершится' });
  const extend = warning.getByRole('button', { name: 'Продлить сессию' });
  await expect(extend).toBeFocused();
  const warningButton = await extend.elementHandle();
  await page.evaluate(() => {
    document.addEventListener('focusin', ({ target }) => {
      if (target instanceof HTMLButtonElement && target.textContent?.trim() === 'Войти снова') {
        target.dataset.focusCount = String(Number(target.dataset.focusCount ?? '0') + 1);
      }
    });
  });

  expired = true;
  await page.evaluate(() => window.dispatchEvent(new Event('pageshow')));
  const ended = page.getByRole('dialog', { name: 'Сессия завершена' });
  const login = ended.getByRole('button', { name: 'Войти снова' });
  await expect(login).toBeFocused();
  await expect(ended).toHaveAccessibleDescription('Войдите снова, чтобы продолжить работу.');
  expect(await warningButton?.evaluate((button) => button.isConnected)).toBe(false);
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(login).toHaveAttribute('data-focus-count', '1');
  await expect(page.getByRole('heading', { name: 'Обзор' })).toHaveCount(0);
  await page.keyboard.press('Tab');
  await expect(login).toBeFocused();
});

test('повторный вход в другой вкладке восстанавливает исходный маршрут', async ({ page, context }) => {
  await loginThroughLanding(page);
  const path = '/app/settings/connections';
  await page.goto(path);
  const heading = page.getByRole('heading', { name: 'Подключения', level: 1 });
  await expect(heading).toBeVisible();

  await page.route(timingPath, (route) => route.fulfill({ status: 401 }));
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  await expect(page.getByRole('dialog', { name: 'Сессия завершена' })).toBeVisible();
  await expect(heading).toHaveCount(0);

  // Изолируем ветку, где RealtimeProvider ещё не получил анонимную сессию.
  // После смены cookie первая вкладка снова читает сессию и данные через настоящий BFF.
  const sessionPath = '**/bff/auth/session';
  await page.route(sessionPath, (route) => route.abort('failed'));
  await context.clearCookies({ name: SESSION_COOKIE });
  await context.clearCookies({ name: IDENTITY_COOKIE });
  const other = await context.newPage();
  await loginThroughLanding(other);
  await expect(page.getByRole('dialog', { name: 'Сессия завершена' })).toBeVisible();

  const sessionCheck = page.waitForResponse((response) =>
    new URL(response.url()).pathname === '/bff/auth/session' && response.ok(),
  );
  const accountRead = page.waitForRequest((request) =>
    new URL(request.url()).pathname === '/bff/me/exchange-accounts' && request.method() === 'GET',
  );
  await page.unroute(sessionPath);
  await page.unroute(timingPath);
  await page.bringToFront();
  await page.evaluate(() => window.dispatchEvent(new Event('focus')));
  const [session] = await Promise.all([sessionCheck, accountRead]);
  expect(await session.json()).toMatchObject({ authenticated: true });
  await expect(page).toHaveURL(new RegExp(`${path}$`));
  await expect(heading).toBeVisible();
  await expect(page.getByRole('dialog', { name: 'Сессия завершена' })).toBeHidden();
});
