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
});
