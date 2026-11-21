import { describe, expect, it, vi } from 'vitest';
import { createInvalidationScheduler, throwIfNetworkFailure } from './invalidationScheduler';

describe('invalidation scheduler', () => {
  it('повтор recovery вытесняет удерживаемый фоновый GET', async () => {
    vi.useFakeTimers();
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true);
    let release!: () => void;
    const previous = new Promise<void>((resolve) => { release = resolve; });
    const publish = vi.fn();
    const read = vi.fn<(signal: AbortSignal) => Promise<void>>()
      .mockRejectedValueOnce(new TypeError('network'))
      .mockImplementationOnce(async (signal) => {
        await previous;
        if (!signal.aborted) publish('старый снимок');
      })
      .mockImplementationOnce(async () => { publish('восстановленный снимок'); });
    const scheduler = createInvalidationScheduler(read);
    try {
      scheduler.recover();
      await vi.advanceTimersByTimeAsync(20);
      scheduler.invalidate();
      const background = read.mock.calls[1]![0];
      await vi.advanceTimersByTimeAsync(230);
      expect(background.aborted).toBe(true);
      expect(read).toHaveBeenCalledTimes(3);
      release();
      await vi.advanceTimersByTimeAsync(0);
      expect(publish.mock.calls).toEqual([['восстановленный снимок']]);
    } finally {
      release();
      scheduler.dispose();
      online.mockRestore();
      vi.useRealTimers();
    }
  });

  it('восстанавливается после второго сетевого отказа и нового online', async () => {
    vi.useFakeTimers();
    let online = false;
    const onlineState = vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online);
    const read = vi.fn().mockRejectedValueOnce(new TypeError('network'))
      .mockImplementationOnce(async () => { online = false; throw new TypeError('network again'); })
      .mockResolvedValue(undefined);
    const scheduler = createInvalidationScheduler(read);
    try {
      scheduler.recover();
      await vi.advanceTimersByTimeAsync(1000);
      expect(read).toHaveBeenCalledTimes(1);
      online = true;
      window.dispatchEvent(new Event('online'));
      await vi.advanceTimersByTimeAsync(250);
      expect(read).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(1000);
      expect(read).toHaveBeenCalledTimes(2);
      online = true;
      window.dispatchEvent(new Event('online'));
      window.dispatchEvent(new Event('online'));
      await vi.advanceTimersByTimeAsync(500);
      expect(read).toHaveBeenCalledTimes(3);
    } finally {
      scheduler.dispose();
      onlineState.mockRestore();
      vi.useRealTimers();
    }
  });

  it('ограничивает один цикл восстановления двумя автоматическими повторами', async () => {
    vi.useFakeTimers();
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true);
    const read = vi.fn().mockRejectedValue(new TypeError('network'));
    const scheduler = createInvalidationScheduler(read);
    try {
      scheduler.recover();
      await vi.advanceTimersByTimeAsync(249);
      expect(read).toHaveBeenCalledOnce();
      await vi.advanceTimersByTimeAsync(1);
      expect(read).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(499);
      expect(read).toHaveBeenCalledTimes(2);
      await vi.advanceTimersByTimeAsync(1);
      expect(read).toHaveBeenCalledTimes(3);
      window.dispatchEvent(new Event('online'));
      await vi.advanceTimersByTimeAsync(2000);
      expect(read).toHaveBeenCalledTimes(3);
    } finally {
      scheduler.dispose();
      online.mockRestore();
      vi.useRealTimers();
    }
  });

  it.each(['cancelPending', 'dispose', 'recover'] as const)('убирает старый отложенный повтор при %s', async (operation) => {
    vi.useFakeTimers();
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true);
    const read = vi.fn().mockRejectedValueOnce(new TypeError('network')).mockResolvedValue(undefined);
    const scheduler = createInvalidationScheduler(read);
    try {
      scheduler.recover();
      await vi.advanceTimersByTimeAsync(100);
      expect(read).toHaveBeenCalledOnce();
      scheduler[operation]();
      await vi.advanceTimersByTimeAsync(1000);
      expect(read).toHaveBeenCalledTimes(operation === 'recover' ? 2 : 1);
    } finally {
      scheduler.dispose();
      online.mockRestore();
      vi.useRealTimers();
    }
  });

  it('coalesces a burst during a read and performs the final read', async () => {
    let finish!: () => void;
    const read = vi.fn().mockImplementationOnce(() => new Promise<void>((resolve) => { finish = resolve; })).mockResolvedValue(undefined);
    const scheduler = createInvalidationScheduler(read);
    scheduler.invalidate();
    scheduler.invalidate();
    scheduler.invalidate();
    expect(read).toHaveBeenCalledTimes(1);
    finish();
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    scheduler.dispose();
  });

  it('does not run queued work after disposal', async () => {
    let finish!: () => void;
    const read = vi.fn().mockImplementation(() => new Promise<void>((resolve) => { finish = resolve; }));
    const scheduler = createInvalidationScheduler(read);
    scheduler.invalidate();
    scheduler.invalidate();
    scheduler.dispose();
    finish();
    await Promise.resolve();
    expect(read).toHaveBeenCalledTimes(1);
  });
  it('recovers without waiting for an aborted read and disposes the new read', async () => {
    let rejectOld!: (reason: Error) => void;
    const read = vi.fn().mockImplementationOnce(() => new Promise<void>((_, reject) => { rejectOld = reject; }))
      .mockImplementation(() => new Promise<void>(() => {}));
    const scheduler = createInvalidationScheduler(read);
    scheduler.invalidate();
    const first = read.mock.calls[0]![0] as AbortSignal;
    scheduler.recover();
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    const second = read.mock.calls[1]![0] as AbortSignal;
    expect(first.aborted).toBe(true);
    expect(second.aborted).toBe(false);
    rejectOld(new Error('late'));
    scheduler.dispose();
    expect(second.aborted).toBe(true);
    scheduler.invalidate();
    scheduler.recover();
    await Promise.resolve();
    expect(read).toHaveBeenCalledTimes(2);
  });

  it('retries one failed offline recovery after the browser returns online', async () => {
    let online = false;
    const onlineState = vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online);
    const read = vi.fn().mockImplementationOnce(async (signal: AbortSignal) => {
      throwIfNetworkFailure(new TypeError('network'), signal);
    }).mockResolvedValue(undefined);
    const scheduler = createInvalidationScheduler(read);
    scheduler.recover();
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(1));
    await Promise.resolve();
    online = true;
    window.dispatchEvent(new Event('online'));
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    window.dispatchEvent(new Event('online'));
    expect(read).toHaveBeenCalledTimes(2);
    scheduler.dispose();
    onlineState.mockRestore();
  });

  it('does not retry an authentication error or a disposed recovery', async () => {
    const onlineState = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false);
    const read = vi.fn().mockRejectedValueOnce(new Error('unauthorized'))
      .mockRejectedValueOnce(new TypeError('network'));
    const scheduler = createInvalidationScheduler(read);
    scheduler.recover();
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(1));
    await Promise.resolve();
    window.dispatchEvent(new Event('online'));
    expect(read).toHaveBeenCalledTimes(1);
    scheduler.recover();
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    await Promise.resolve();
    scheduler.dispose();
    window.dispatchEvent(new Event('online'));
    expect(read).toHaveBeenCalledTimes(2);
    onlineState.mockRestore();
  });

  it('retries once when the network failure arrives after the browser is online', async () => {
    const onlineState = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true);
    const read = vi.fn().mockImplementationOnce(async (signal: AbortSignal) => {
      throwIfNetworkFailure(new TypeError('network'), signal);
    }).mockResolvedValue(undefined);
    const scheduler = createInvalidationScheduler(read);
    scheduler.recover();
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    scheduler.dispose();
    onlineState.mockRestore();
  });

  it('keeps the online recovery pending when an invalidation arrives offline', async () => {
    let online = false;
    const onlineState = vi.spyOn(navigator, 'onLine', 'get').mockImplementation(() => online);
    const read = vi.fn().mockRejectedValueOnce(new TypeError('network'))
      .mockRejectedValueOnce(new TypeError('network')).mockResolvedValue(undefined);
    const scheduler = createInvalidationScheduler(read);
    scheduler.recover();
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(1));
    await Promise.resolve();
    scheduler.invalidate();
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(2));
    await Promise.resolve();
    online = true;
    window.dispatchEvent(new Event('online'));
    await vi.waitFor(() => expect(read).toHaveBeenCalledTimes(3));
    scheduler.dispose();
    onlineState.mockRestore();
  });
});
