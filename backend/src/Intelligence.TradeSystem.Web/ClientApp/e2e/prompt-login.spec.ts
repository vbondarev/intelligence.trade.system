import { expect, test } from '@playwright/test';
import {
  buildPromptLoginAuthorizeUrl,
  findCookie,
  IDENTITY_COOKIE,
  identityBaseUrl,
  loginThroughLanding,
} from './support';

test('prompt=login shows the Identity login form despite an existing SSO session', async ({ page, context }) => {
  await loginThroughLanding(page);
  expect(await findCookie(context, IDENTITY_COOKIE)).toBeDefined();

  await page.goto(buildPromptLoginAuthorizeUrl());

  await page.waitForURL((url) => url.origin === identityBaseUrl && url.pathname === '/account/login');
  await expect(page.locator('input[name="password"]')).toBeVisible();
});
