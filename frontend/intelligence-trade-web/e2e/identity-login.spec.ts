import { randomBytes } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { credentials, identityBaseUrl, openIdentityLoginThroughLanding, webBaseUrl } from './support';

const identityLoginUrl = `${identityBaseUrl}/account/login`;
const genericLoginError = 'Неверное имя пользователя или пароль.';

const viewports = [
  { name: 'mobile', width: 390, height: 844 },
  { name: 'desktop', width: 1440, height: 900 },
];

function usernameInput(page: Page) {
  return page.getByLabel('Имя пользователя', { exact: true });
}

function passwordInput(page: Page) {
  return page.getByLabel('Пароль', { exact: true });
}

for (const viewport of viewports) {
  test(`Identity login page is styled by its own stylesheet on ${viewport.name} ${viewport.width}x${viewport.height}`, async ({
    page,
  }) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await openIdentityLoginThroughLanding(page);

    const card = page.locator('.auth-card');
    await expect(card).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Вход' })).toBeVisible();
    await expect(usernameInput(page)).toBeVisible();
    await expect(passwordInput(page)).toBeVisible();
    await expect(page.getByRole('button', { name: 'Войти' })).toBeVisible();

    // Identity самодостаточен: единственный stylesheet — собственный, без frontend origin, CDN и скриптов.
    const stylesheets = await page
      .locator('link[rel="stylesheet"]')
      .evaluateAll((links) => links.map((link) => link.getAttribute('href')));
    expect(stylesheets).toEqual(['/css/identity.css']);
    await expect(page.locator('script')).toHaveCount(0);

    const stylesheet = await page.request.get(`${identityBaseUrl}/css/identity.css`);
    expect(stylesheet.status()).toBe(200);
    expect(stylesheet.headers()['content-type']).toContain('text/css');

    await expect(page.locator('body')).toHaveCSS('background-color', 'rgb(11, 15, 20)');
    await expect(card).toHaveCSS('background-color', 'rgb(18, 24, 33)');
    expect(await card.evaluate((element) => getComputedStyle(element).borderTopLeftRadius)).not.toBe('0px');

    const overflow = await page.evaluate(() => ({
      scrollWidth: document.documentElement.scrollWidth,
      innerWidth: window.innerWidth,
    }));
    expect(overflow.scrollWidth).toBeLessThanOrEqual(overflow.innerWidth);
  });
}

test('wrong password keeps the user on the Identity form with one generic error, then login succeeds', async ({ page }) => {
  const { username, password } = credentials();
  const wrongPassword = `wrong-${randomBytes(12).toString('hex')}`;

  await openIdentityLoginThroughLanding(page);
  const returnUrl = await page.locator('input[name="returnUrl"]').inputValue();
  expect(returnUrl).not.toBe('');

  await usernameInput(page).fill(username);
  await passwordInput(page).fill(wrongPassword);
  const [failedLogin] = await Promise.all([
    page.waitForResponse(
      (response) => response.url() === identityLoginUrl && response.request().method() === 'POST',
    ),
    page.getByRole('button', { name: 'Войти' }).click(),
  ]);

  expect(failedLogin.status()).toBe(400);
  await expect(page).toHaveURL(identityLoginUrl);
  const alert = page.getByRole('alert');
  await expect(alert).toHaveText(genericLoginError);
  await expect(alert).toHaveCSS('background-color', 'rgb(42, 21, 23)');
  await expect(alert).toHaveCSS('color', 'rgb(248, 113, 113)');

  expect(await failedLogin.text()).not.toContain(wrongPassword);
  expect(await page.content()).not.toContain(wrongPassword);
  await expect(usernameInput(page)).toHaveValue(username);
  await expect(passwordInput(page)).toHaveValue('');
  await expect(page.locator('input[name="returnUrl"]')).toHaveValue(returnUrl);

  await passwordInput(page).fill(password);
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.waitForURL(`${webBaseUrl}/app`);
  await expect(page.getByRole('heading', { name: 'Обзор' })).toBeVisible();
});
