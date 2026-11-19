import { randomUUID } from 'node:crypto';
import { expect, test } from '@playwright/test';
import { loginThroughLanding } from '../e2e/support';

const target = 'https://localtest.me:18994';
const attacker = 'https://127.0.0.1.sslip.io:18994';

test('real BFF session is Lax on HTTPS, including cross-site and navigation', async ({ page }) => {
  await loginThroughLanding(page);
  await page.goto(`${target}/__security/control`);

  async function result(id: string) {
    return page.evaluate(async ({ target, id }) => {
      const response = await fetch(`${target}/__security/results?id=${id}`, { credentials: 'include' });
      return response.json();
    }, { target, id });
  }

  const sameSite = randomUUID();
  await page.evaluate(async ({ target, id }) => {
    await fetch(`${target}/__security/observe?id=${id}`, { credentials: 'include' });
  }, { target, id: sameSite });
  const sameSiteResult = await result(sameSite);
  expect(sameSiteResult.sessionAttributes).toEqual({ secure: true, httpOnly: true, sameSiteLax: true });
  expect(sameSiteResult.observation).toEqual({ session: true, control: true });

  const crossSite = randomUUID();
  await page.goto(`${attacker}/__security/attacker`);
  await page.evaluate(async ({ target, id }) => {
    await fetch(`${target}/__security/observe?id=${id}`, { mode: 'no-cors', credentials: 'include' });
  }, { target, id: crossSite });
  await page.goto(`${target}/__security/attacker`);
  // A missing control means third-party cookies were blocked and the security
  // assertion is inconclusive; it must fail rather than silently pass.
  expect.soft((await result(crossSite)).observation).toEqual({ session: false, control: true });

  const navigation = randomUUID();
  await page.goto(`${attacker}/__security/attacker`);
  const destination = `${target}/__security/observe?id=${navigation}`;
  // Navigate from the other site via a link so the browser has a document initiator.
  await page.evaluate((href) => {
    const link = document.createElement('a');
    link.href = href;
    link.textContent = 'Open the session site';
    document.body.append(link);
  }, destination);
  await Promise.all([
    page.waitForURL(destination),
    page.getByRole('link', { name: 'Open the session site' }).click(),
  ]);
  expect((await result(navigation)).observation).toEqual({ session: true, control: true });
});

test('продление реальной HTTPS-сессии сохраняет защиту cookie и требует CSRF', async ({ page }) => {
  await loginThroughLanding(page);
  const path = '/bff/auth/session/extend';
  const denied = await page.request.post(path);
  expect(denied.status()).toBe(400);
  const csrf = await page.request.get('/bff/auth/antiforgery');
  expect(csrf.ok()).toBe(true);
  const { requestToken } = await csrf.json() as { requestToken: string };
  const extended = await page.request.post(path, { headers: { 'X-CSRF-TOKEN': requestToken } });
  expect(extended.ok()).toBe(true);
  const header = extended.headersArray().find(({ name, value }) =>
    name.toLowerCase() === 'set-cookie' && value.startsWith('TradeSystem.Bff.Session='))?.value ?? '';
  expect({
    secure: /;\s*secure\b/i.test(header),
    httpOnly: /;\s*httponly\b/i.test(header),
    sameSiteLax: /;\s*samesite=lax\b/i.test(header),
  }).toEqual({ secure: true, httpOnly: true, sameSiteLax: true });
  const session = await page.request.get('/bff/auth/session');
  expect((await session.json() as { authenticated: boolean }).authenticated).toBe(true);
});
