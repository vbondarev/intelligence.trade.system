import { expect, test } from '@playwright/test';
import { identityBaseUrl, webBaseUrl } from './support';

// Frontend service — единственный browser-facing origin: React routes обслуживает он сам,
// а /bff/** и OIDC callbacks проксирует во внутренний BFF.
test('frontend service serves React routes and proxies the BFF boundary on the same origin', async ({ request }) => {
  for (const path of ['/', '/app', '/app/nested/route']) {
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

  const login = await request.get('/bff/auth/login?returnUrl=/app', { maxRedirects: 0 });
  expect(login.status()).toBe(302);
  const authorize = new URL(login.headers()['location']);
  expect(authorize.origin).toBe(identityBaseUrl);
  expect(authorize.searchParams.get('redirect_uri')).toBe(`${webBaseUrl}/signin-oidc`);
});
