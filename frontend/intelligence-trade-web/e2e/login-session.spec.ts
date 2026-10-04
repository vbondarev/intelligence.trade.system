import { expect, test } from '@playwright/test';
import { findCookie, loginThroughLanding, SESSION_COOKIE } from './support';

test('login keeps the session across reload without exposing tokens to JavaScript', async ({ page, context }) => {
  await loginThroughLanding(page);

  await page.reload();
  await expect(page.getByRole('heading', { name: 'Обзор' })).toBeVisible();
  await expect(page).toHaveURL(/\/app$/);

  const sessionCookie = await findCookie(context, SESSION_COOKIE);
  expect(sessionCookie).toBeDefined();
  expect(sessionCookie?.httpOnly).toBe(true);
  expect(sessionCookie?.sameSite).toBe('Lax');

  const browserState = await page.evaluate(() => ({
    documentCookie: document.cookie,
    localStorage: JSON.stringify({ ...window.localStorage }),
    sessionStorage: JSON.stringify({ ...window.sessionStorage }),
  }));
  expect(browserState.documentCookie).not.toContain(SESSION_COOKIE);
  expect(browserState.documentCookie).not.toMatch(/token/i);
  expect(browserState.localStorage).not.toMatch(/token/i);
  expect(browserState.sessionStorage).not.toMatch(/token/i);

  const session = await page.request.get('/bff/auth/session');
  expect(session.ok()).toBe(true);
  const sessionText = await session.text();
  expect(sessionText).not.toMatch(/token/i);
  expect(JSON.parse(sessionText)).toMatchObject({ authenticated: true });
});
