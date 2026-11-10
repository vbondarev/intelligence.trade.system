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

function positionPhase(userId: string, accountId: string, positionId: string, phase: string, eventId: string) {
  const args = ['compose', ...(project ? ['-p', project] : []), '-f', composeFile, '--profile', 'ci',
    'run', '--rm', '--no-deps', '-e', 'REALTIME_E2E_SCENARIO=position', '-e', `REALTIME_E2E_USER_ID=${userId}`,
    '-e', `REALTIME_E2E_ACCOUNT_ID=${accountId}`, '-e', `REALTIME_E2E_POSITION_ID=${positionId}`,
    '-e', `REALTIME_E2E_PHASE=${phase}`, '-e', `REALTIME_E2E_EVENT_ID=${eventId}`, 'realtime-e2e-driver'];
  execFileSync('docker', args, { cwd: repository, timeout: 120000, stdio: 'pipe' });
}

test('позиция обновляется через outbox и восстанавливается после пропущенного события', async ({ page, context, browser }) => {
  test.skip(!enabled, 'Требуется disposable Compose с realtime E2E driver.');
  test.setTimeout(240000);
  await page.addInitScript(() => {
    const Original = window.WebSocket;
    class Tracked extends Original {
      constructor(url: string | URL, protocols?: string | string[]) {
        super(url, protocols);
        if (String(url).includes('/bff/hubs/v1/updates')) window.__realtimeTestSocket = this;
      }
    }
    window.WebSocket = Tracked;
  });
  let ready = 0;
  const positionEvents: string[] = [];
  let listGets = 0;
  let detailGets = 0;
  const mutations: string[] = [];
  const accountId = randomUUID();
  const positionId = randomUUID();
  page.on('websocket', (socket) => {
    if (socket.url().includes('/bff/hubs/v1/updates')) socket.on('framereceived', ({ payload }) => {
      if (typeof payload !== 'string') return;
      if (payload.includes('bff.realtime.ready')) ready++;
      if (payload.includes('position.updated') && payload.includes(positionId)) positionEvents.push(payload);
    });
  });
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (request.method() === 'GET' && url.pathname === '/bff/me/positions') listGets++;
    if (request.method() === 'GET' && url.pathname === `/bff/me/positions/${positionId}`) detailGets++;
    if (request.method() === 'POST' && /\/(sync|evaluation)(\?|$)/.test(request.url())) mutations.push(request.url());
  });
  await loginThroughLanding(page);
  await expect.poll(() => ready, { timeout: 20000 }).toBeGreaterThan(0);
  const userId = await page.evaluate(async () => {
    const session = await (await fetch('/bff/auth/session', { credentials: 'same-origin' })).json() as { user: { userId: string } };
    return session.user.userId;
  });
  try {
    const initialEvent = randomUUID();
    positionPhase(userId, accountId, positionId, 'initial', initialEvent);
    positionPhase(userId, accountId, positionId, 'wait-processed', initialEvent);
    await expect.poll(() => positionEvents.some((event) => event.includes(initialEvent)), { timeout: 30000 }).toBe(true);
    await page.goto(`/app?account=${accountId}&side=long&symbol=BTCUSDT`);
    const card = page.getByRole('link', { name: /BTCUSDT.*1.*Long/i });
    await expect(card).toBeVisible({ timeout: 30000 });
    await expect.poll(() => ready, { timeout: 20000 }).toBeGreaterThan(1);
    await page.evaluate(() => { window.__realtimePageMarker = 'position-document'; });
    const beforeList = listGets;
    await expect.poll(() => page.evaluate(() => window.__realtimeTestSocket?.readyState)).toBe(1);
    const changedEvent = randomUUID();
    positionPhase(userId, accountId, positionId, 'changed', changedEvent);
    await expect.poll(() => positionEvents.some((event) => event.includes(changedEvent)), { timeout: 30000 }).toBe(true);
    await expect.poll(() => listGets).toBeGreaterThan(beforeList);
    const updatedCard = page.getByRole('link', { name: /BTCUSDT.*2.*Long/i });
    await expect(updatedCard).toBeVisible();
    const url = new URL(page.url());
    expect(url.searchParams.get('account')).toBe(accountId);
    expect(url.searchParams.get('side')).toBe('long');
    expect(url.searchParams.get('symbol')).toBe('BTCUSDT');
    await updatedCard.click();
    await expect(page).toHaveURL(new RegExp(`/app/positions/${positionId}`));
    await expect(page.getByRole('heading', { name: /BTCUSDT.*2/ })).toBeVisible();
    const observerContext = await browser.newContext({ baseURL: process.env.WEB_BASE_URL ?? 'http://localhost:8082' });
    const observer = await observerContext.newPage();
    let observerReady = false;
    const observedEvents: string[] = [];
    observer.on('websocket', (socket) => {
      if (socket.url().includes('/bff/hubs/v1/updates')) socket.on('framereceived', ({ payload }) => {
        if (typeof payload !== 'string') return;
        if (payload.includes('bff.realtime.ready')) observerReady = true;
        if (payload.includes('position.updated') && payload.includes(positionId)) observedEvents.push(payload);
      });
    });
    await loginThroughLanding(observer);
    await expect.poll(() => observerReady, { timeout: 20000 }).toBe(true);
    await expect.poll(() => page.evaluate(() => window.__realtimeTestSocket?.readyState)).toBe(1);
    const beforeDetail = detailGets;
    const beforeReady = ready;
    await context.setOffline(true);
    await page.evaluate(async () => {
      const socket = window.__realtimeTestSocket;
      if (!socket) throw new Error('Realtime WebSocket отсутствует.');
      if (socket.readyState === WebSocket.CLOSED) return;
      await new Promise<void>((resolve) => { socket.addEventListener('close', () => resolve(), { once: true }); socket.close(); });
    });
    expect(await page.evaluate(() => window.__realtimeTestSocket?.readyState)).toBe(3);
    const eventId = randomUUID();
    positionPhase(userId, accountId, positionId, 'recovered', eventId);
    positionPhase(userId, accountId, positionId, 'wait-processed', eventId);
    await expect.poll(() => observedEvents.some((event) => event.includes(eventId)), { timeout: 30000 }).toBe(true);
    await observerContext.close();
    await context.setOffline(false);
    await expect.poll(() => ready, { timeout: 45000 }).toBeGreaterThan(beforeReady);
    await expect.poll(() => detailGets).toBeGreaterThan(beforeDetail);
    await expect(page.getByRole('heading', { name: /BTCUSDT.*3/ })).toBeVisible({ timeout: 30000 });
    expect(await page.evaluate(() => window.__realtimePageMarker)).toBe('position-document');
    expect(mutations).toEqual([]);
  } finally {
    await context.setOffline(false);
    positionPhase(userId, accountId, positionId, 'cleanup', randomUUID());
  }
});
