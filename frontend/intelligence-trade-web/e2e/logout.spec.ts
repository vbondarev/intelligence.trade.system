import { expect, test } from '@playwright/test';
import { findCookie, IDENTITY_COOKIE, identityBaseUrl, loginThroughLanding, SESSION_COOKIE, webBaseUrl } from './support';

test('logout ends both BFF and Identity sessions', async ({ page, context }) => {
  await loginThroughLanding(page);
  expect(await findCookie(context, IDENTITY_COOKIE)).toBeDefined();

  await page.getByRole('button', { name: 'Выйти' }).click();
  await page.waitForURL(`${webBaseUrl}/`);
  await expect(page.getByRole('button', { name: 'Войти' })).toBeVisible();

  expect(await findCookie(context, SESSION_COOKIE)).toBeUndefined();
  expect(await findCookie(context, IDENTITY_COOKIE)).toBeUndefined();

  const session = await page.request.get('/bff/auth/session');
  expect(await session.json()).toEqual({ authenticated: false });

  await page.getByRole('button', { name: 'Войти' }).click();
  await page.waitForURL((url) => url.origin === identityBaseUrl && url.pathname === '/account/login');
  await expect(page.locator('input[name="password"]')).toBeVisible();
});
