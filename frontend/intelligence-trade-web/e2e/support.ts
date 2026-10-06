import { createHash, randomBytes } from 'node:crypto';
import { expect, type BrowserContext, type Page } from '@playwright/test';

export const SESSION_COOKIE = 'TradeSystem.Bff.Session';
export const IDENTITY_COOKIE = 'TradeSystem.Identity';

export const webBaseUrl = process.env.WEB_BASE_URL ?? 'http://localhost:8082';
export const identityBaseUrl = process.env.IDENTITY_BASE_URL ?? 'http://localhost:8081';
export const webClientId = process.env.E2E_CLIENT_ID ?? 'trade-web-bff';

function requireEnvironment(name: string): string {
  const value = process.env[name];
  if (value === undefined || value.length === 0) {
    throw new Error(`${name} must be set for browser E2E.`);
  }

  return value;
}

export function credentials() {
  return {
    username: process.env.E2E_USERNAME ?? 'trade-dev-user',
    password: requireEnvironment('E2E_PASSWORD'),
  };
}

export async function submitIdentityLogin(page: Page) {
  const { username, password } = credentials();
  await expect(page.locator('input[name="username"]')).toBeVisible();
  await page.locator('input[name="username"]').fill(username);
  await page.locator('input[name="password"]').fill(password);
  await page.getByRole('button', { name: 'Войти' }).click();
}

export async function openIdentityLoginThroughLanding(page: Page) {
  await page.goto('/');
  await page.getByRole('button', { name: 'Войти' }).click();
  await page.waitForURL((url) => url.origin === identityBaseUrl && url.pathname === '/account/login');
}

export async function loginThroughLanding(page: Page) {
  await openIdentityLoginThroughLanding(page);
  await submitIdentityLogin(page);
  await page.waitForURL(`${webBaseUrl}/app`);
  await expect(page.getByRole('heading', { name: 'Обзор' })).toBeVisible();
}

export async function findCookie(context: BrowserContext, name: string) {
  const cookies = await context.cookies([webBaseUrl, identityBaseUrl]);
  return cookies.find((cookie) => cookie.name === name);
}

export function buildPromptLoginAuthorizeUrl(): string {
  const codeVerifier = randomBytes(32).toString('base64url');
  const codeChallenge = createHash('sha256').update(codeVerifier).digest('base64url');
  const query = new URLSearchParams({
    client_id: webClientId,
    redirect_uri: `${webBaseUrl}/signin-oidc`,
    response_type: 'code',
    scope: 'openid offline_access trade.api',
    code_challenge: codeChallenge,
    code_challenge_method: 'S256',
    state: randomBytes(16).toString('base64url'),
    nonce: randomBytes(16).toString('base64url'),
    prompt: 'login',
  });

  return `${identityBaseUrl}/connect/authorize?${query.toString()}`;
}
