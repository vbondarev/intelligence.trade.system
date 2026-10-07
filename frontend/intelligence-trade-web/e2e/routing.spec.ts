import { expect, test } from '@playwright/test';
import { identityBaseUrl, webBaseUrl } from './support';

// Frontend service — единственный browser-facing origin: React routes обслуживает он сам,
// а /bff/** и OIDC callbacks проксирует во внутренний BFF.
test('frontend service serves React routes and proxies the BFF boundary on the same origin', async ({ request }) => {
  for (const path of ['/', '/app', '/app/settings/connections', '/app/nested/route']) {
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
    '/bff/me/exchange-accounts/not-a-guid',
    '/bff/me/exchange-accounts/2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6/sync',
  ]) {
    const unplanned = await request.get(path);
    expect(unplanned.status(), path).toBe(404);
    expect(await unplanned.text(), path).not.toContain('<div id="root">');
  }

  const login = await request.get('/bff/auth/login?returnUrl=/app', { maxRedirects: 0 });
  expect(login.status()).toBe(302);
  const authorize = new URL(login.headers()['location']);
  expect(authorize.origin).toBe(identityBaseUrl);
  expect(authorize.searchParams.get('redirect_uri')).toBe(`${webBaseUrl}/signin-oidc`);
});
