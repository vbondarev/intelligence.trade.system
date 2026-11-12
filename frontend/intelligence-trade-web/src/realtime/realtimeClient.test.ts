import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { RealtimeInvalidation, RealtimeState } from './realtimeTypes';

const fakes = vi.hoisted(() => ({ failStart: false, starts: [] as Array<() => Promise<void>>, connections: [] as Array<{
  handlers: Map<string, (payload?: never) => void>;
  close?: () => void;
  start: ReturnType<typeof vi.fn>;
  stop: ReturnType<typeof vi.fn>;
}> }));

vi.mock('@microsoft/signalr', () => ({
  HttpTransportType: { WebSockets: 1 },
  LogLevel: { None: 6 },
  HubConnectionBuilder: class {
    withUrl() { return this; }
    configureLogging() { return this; }
    build() {
      const fake = {
        handlers: new Map<string, (payload?: never) => void>(),
        close: undefined as (() => void) | undefined,
        start: vi.fn().mockImplementation(fakes.starts[fakes.connections.length] ?? (() => fakes.failStart ? Promise.reject(new Error('offline')) : Promise.resolve())),
        stop: vi.fn().mockResolvedValue(undefined),
        on(name: string, handler: (payload?: never) => void) { this.handlers.set(name, handler); },
        onclose(handler: () => void) { this.close = handler; },
      };
      fakes.connections.push(fake);
      return fake;
    }
  },
}));

vi.unmock('./realtimeClient');
import { createRealtimeClient } from './realtimeClient';

describe('realtime transport', () => {
  beforeEach(() => { fakes.connections.length = 0; fakes.starts.length = 0; fakes.failStart = false; vi.useFakeTimers(); });
  afterEach(() => vi.useRealTimers());

  it('recovers after ready and forwards only valid invalidations', async () => {
    const states: RealtimeState[] = [];
    const events: RealtimeInvalidation[] = [];
    const client = createRealtimeClient((state) => states.push(state), (event) => events.push(event), async () => true);
    await client.start();
    const first = fakes.connections[0]!;
    expect(events).toEqual([]);
    first.handlers.get('bff.realtime.ready')?.();
    first.handlers.get('position.updated')?.({ positionId: 'p1' } as never);
    first.handlers.get('portfolio.updated')?.({ exchangeAccountId: 'a1' } as never);
    first.handlers.get('evaluation.updated')?.({ positionId: 123 } as never);
    expect(events).toEqual([{ type: 'recovery' }, { type: 'position.updated', positionId: 'p1' }, { type: 'portfolio.updated', exchangeAccountId: 'a1' }]);
    expect(states).toEqual(['connecting', 'ready']);
    await client.stop();
    first.handlers.get('position.updated')?.({ positionId: 'late' } as never);
    expect(events).toHaveLength(3);
  });

  it('revalidates after close, reconnects and recovers again', async () => {
    const events: RealtimeInvalidation[] = [];
    const check = vi.fn().mockResolvedValue(true);
    const client = createRealtimeClient(() => {}, (event) => events.push(event), check);
    await client.start();
    fakes.connections[0]!.handlers.get('bff.realtime.ready')?.();
    fakes.connections[0]!.close?.();
    await vi.advanceTimersByTimeAsync(1000);
    expect(check).toHaveBeenCalledTimes(1);
    expect(fakes.connections).toHaveLength(2);
    fakes.connections[1]!.handlers.get('bff.realtime.ready')?.();
    expect(events).toEqual([{ type: 'recovery' }, { type: 'recovery' }]);
    await client.stop();
  });

  it('does not retry after authorization loss or disposal', async () => {
    const check = vi.fn().mockResolvedValue(false);
    const client = createRealtimeClient(() => {}, () => {}, check);
    await client.start();
    fakes.connections[0]!.close?.();
    await vi.advanceTimersByTimeAsync(60000);
    expect(fakes.connections).toHaveLength(1);
    await client.stop();
    expect(vi.getTimerCount()).toBe(0);
  });
  it('bounds retries after initial connection failures', async () => {
    fakes.failStart = true;
    const states: RealtimeState[] = [];
    const client = createRealtimeClient((state) => states.push(state), () => {}, async () => true);
    await client.start();
    await vi.advanceTimersByTimeAsync(200000);
    expect(fakes.connections).toHaveLength(9);
    expect(states.at(-1)).toBe('offline');
    expect(vi.getTimerCount()).toBe(0);
    await client.stop();
  });
  it.each([false, true])('T01/T02: ожидает pending retry перед stop (initial failure: %s)', async (initialFailure) => {
    let finishStart!: () => void;
    let finishStop!: () => void;
    const pendingStart = new Promise<void>((resolve) => { finishStart = resolve; });
    const pendingStop = new Promise<void>((resolve) => { finishStop = resolve; });
    fakes.starts.push(
      () => initialFailure ? Promise.reject(new Error('offline')) : Promise.resolve(),
      () => pendingStart,
    );
    const client = createRealtimeClient(() => {}, () => {}, async () => true);
    await client.start();
    if (!initialFailure) fakes.connections[0]!.close?.();
    await vi.advanceTimersByTimeAsync(1000);
    const retry = fakes.connections[1]!;
    expect(retry.start).toHaveBeenCalledOnce();
    retry.stop.mockReturnValue(pendingStop);
    let stopped = false;
    const stopping = client.stop().then(() => { stopped = true; });
    await Promise.resolve();
    expect(retry.stop).not.toHaveBeenCalled();
    expect(stopped).toBe(false);
    finishStart();
    await vi.waitFor(() => expect(retry.stop).toHaveBeenCalledOnce());
    expect(stopped).toBe(false);
    finishStop();
    await stopping;
    expect(stopped).toBe(true);
    expect(retry.stop).toHaveBeenCalledOnce();
  });

  it('T03: отклонение pending retry завершает disposal без нового retry', async () => {
    let rejectStart!: (error: Error) => void;
    const pendingStart = new Promise<void>((_, reject) => { rejectStart = reject; });
    fakes.starts.push(() => Promise.reject(new Error('offline')), () => pendingStart);
    const states: RealtimeState[] = [];
    const client = createRealtimeClient((state) => states.push(state), () => {}, async () => true);
    await client.start();
    await vi.advanceTimersByTimeAsync(1000);
    const retry = fakes.connections[1]!;
    expect(retry.start).toHaveBeenCalledOnce();
    const stopping = client.stop();
    expect(retry.stop).not.toHaveBeenCalled();
    rejectStart(new Error('retry failed'));
    await stopping;
    await vi.advanceTimersByTimeAsync(200000);
    expect(fakes.connections).toHaveLength(2);
    expect(vi.getTimerCount()).toBe(0);
    expect(states).not.toContain('ready');
  });

  it('T04: отменяет таймер до повторного подключения', async () => {
    fakes.starts.push(() => Promise.reject(new Error('offline')));
    const client = createRealtimeClient(() => {}, () => {}, async () => true);
    await client.start();
    expect(vi.getTimerCount()).toBe(1);
    await client.stop();
    await vi.advanceTimersByTimeAsync(200000);
    expect(fakes.connections).toHaveLength(1);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('T05: игнорирует callbacks после начала disposal', async () => {
    let finishStart!: () => void;
    fakes.starts.push(() => Promise.resolve(), () => new Promise<void>((resolve) => { finishStart = resolve; }));
    const states: RealtimeState[] = [];
    const events: RealtimeInvalidation[] = [];
    const client = createRealtimeClient((state) => states.push(state), (event) => events.push(event), async () => true);
    await client.start();
    fakes.connections[0]!.close?.();
    await vi.advanceTimersByTimeAsync(1000);
    const retry = fakes.connections[1]!;
    const stopping = client.stop();
    const count = states.length;
    retry.handlers.get('bff.realtime.ready')?.();
    retry.handlers.get('position.updated')?.({ positionId: 'late' } as never);
    retry.close?.();
    finishStart();
    await stopping;
    await vi.advanceTimersByTimeAsync(200000);
    expect(states).toHaveLength(count);
    expect(states).not.toContain('ready');
    expect(events).toEqual([]);
    expect(fakes.connections).toHaveLength(2);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('T06: ошибка stop распространяется и не запускает retry', async () => {
    const client = createRealtimeClient(() => {}, () => {}, async () => true);
    await client.start();
    const connection = fakes.connections[0]!;
    connection.stop.mockRejectedValue(new Error('stop failed'));
    await expect(client.stop()).rejects.toThrow('stop failed');
    connection.close?.();
    await vi.advanceTimersByTimeAsync(200000);
    expect(fakes.connections).toHaveLength(1);
    expect(vi.getTimerCount()).toBe(0);
  });

});
