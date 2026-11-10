import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { resolve } from 'node:path';
import { expect, test } from '@playwright/test';
import { loginThroughLanding } from './support';

declare global {
  interface Window { __realtimeTestSocket?: WebSocket; __realtimePageMarker?: string }
}

const enabled = process.env.REALTIME_E2E_ENABLED === 'true';
const composeFile = process.env.REALTIME_E2E_COMPOSE_FILE ?? 'backend/compose.yaml';
const project = process.env.REALTIME_E2E_COMPOSE_PROJECT;
const repository = resolve(process.cwd(), '../..');

function changeState(userId: string, accountId: string, name: string) {
  const args = ['compose', ...(project ? ['-p', project] : []), '-f', composeFile, '--profile', 'ci',
    'run', '--rm', '--no-deps', '-e', `REALTIME_E2E_USER_ID=${userId}`,
    '-e', `REALTIME_E2E_ACCOUNT_ID=${accountId}`, '-e', `REALTIME_E2E_NAME=${name}`, 'realtime-e2e-driver'];
  execFileSync('docker', args, { cwd: repository, timeout: 120000, stdio: 'pipe' });
}

test('persisted outbox update and missed event recover through the browser', async ({ page, context }) => {
  test.skip(!enabled, 'Требуется disposable Compose с realtime E2E driver.');
  test.setTimeout(180000);
  await page.addInitScript(() => {
    const OriginalWebSocket = window.WebSocket;
    class TrackedWebSocket extends OriginalWebSocket {
      constructor(url: string | URL, protocols?: string | string[]) {
        super(url, protocols);
        if (String(url).includes('/bff/hubs/v1/updates')) window.__realtimeTestSocket = this;
      }
    }
    window.WebSocket = TrackedWebSocket;
  });
  let readyCount = 0;
  const mutations: string[] = [];
  page.on('websocket', (socket) => {
    if (socket.url().includes('/bff/hubs/v1/updates')) socket.on('framereceived', (frame) => {
      if (typeof frame.payload === 'string' && frame.payload.includes('bff.realtime.ready')) readyCount += 1;
    });
  });
  page.on('request', (request) => {
    if (request.method() === 'POST' && /\/(sync|evaluation)(\?|$)/.test(request.url())) mutations.push(request.url());
  });

  await loginThroughLanding(page);
  await expect.poll(() => readyCount, { timeout: 20000 }).toBeGreaterThan(0);
  const userId = await page.evaluate(async () => {
    const session = await (await fetch('/bff/auth/session', { credentials: 'same-origin' })).json() as { user: { userId: string } };
    return session.user.userId;
  });
  const accountId = randomUUID();
  const suffix = accountId.replaceAll('-', '').slice(0, 8);
  const title = (phase: string) => `${phase} ${suffix}`;
  changeState(userId, accountId, 'G07 initial');
  try {
    await page.goto('/app/settings/connections');
    await expect(page.getByRole('heading', { name: title('G07 initial') })).toBeVisible();
    await expect.poll(() => readyCount, { timeout: 20000 }).toBeGreaterThan(1);
    await page.evaluate(() => { window.__realtimePageMarker = 'same-document'; });
    changeState(userId, accountId, 'G07 changed');
    await expect(page.getByRole('heading', { name: title('G07 changed') })).toBeVisible({ timeout: 30000 });

    const beforeReconnect = readyCount;
    await context.setOffline(true);
    await page.evaluate(() => window.__realtimeTestSocket?.close());
    changeState(userId, accountId, 'G07 recovered');
    await context.setOffline(false);
    await expect.poll(() => readyCount, { timeout: 45000 }).toBeGreaterThan(beforeReconnect);
    await expect(page.getByRole('heading', { name: title('G07 recovered') })).toBeVisible({ timeout: 30000 });
    expect(await page.evaluate(() => window.__realtimePageMarker)).toBe('same-document');
    expect(mutations).toEqual([]);
  } finally {
    changeState(userId, accountId, 'cleanup');
  }
});
