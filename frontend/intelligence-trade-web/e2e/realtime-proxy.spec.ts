import { expect, test } from '@playwright/test';
import { loginThroughLanding } from './support';

test('same-origin WebSocket reaches the BFF and waits for upstream readiness', async ({ page }) => {
  await loginThroughLanding(page);
  const result = await page.evaluate(() => new Promise<string>((resolve, reject) => {
    const socket = new WebSocket(`${location.protocol === 'https:' ? 'wss:' : 'ws:'}//${location.host}/bff/hubs/v1/updates`);
    const timeout = setTimeout(() => { socket.close(); reject(new Error('Realtime readiness timeout')); }, 15000);
    socket.onopen = () => socket.send('{"protocol":"json","version":1}\u001e');
    socket.onerror = () => { clearTimeout(timeout); reject(new Error('WebSocket failed')); };
    socket.onmessage = (event) => {
      if (typeof event.data === 'string' && event.data.includes('bff.realtime.ready')) {
        clearTimeout(timeout);
        socket.close();
        resolve(event.data);
      }
    };
  }));
  expect(result).toContain('bff.realtime.ready');
});

test('authenticated shell establishes its realtime transport', async ({ page }) => {
  let ready = false;
  let connections = 0;
  page.on('websocket', (socket) => {
    if (socket.url().includes('/bff/hubs/v1/updates')) {
      connections += 1;
      socket.on('framereceived', (frame) => {
        if (typeof frame.payload === 'string' && frame.payload.includes('bff.realtime.ready')) ready = true;
      });
    }
  });
  await loginThroughLanding(page);
  await expect.poll(() => ready, { timeout: 15000 }).toBe(true);
  expect(connections).toBe(1);
});
