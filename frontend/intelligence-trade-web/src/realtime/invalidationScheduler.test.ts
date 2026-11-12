import { describe, expect, it, vi } from 'vitest';
import { createInvalidationScheduler } from './invalidationScheduler';

describe('invalidation scheduler', () => {
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
});
