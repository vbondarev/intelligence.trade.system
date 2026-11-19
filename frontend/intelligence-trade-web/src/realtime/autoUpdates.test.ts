import { describe, expect, it, vi } from 'vitest';
import { createAutoUpdates } from './autoUpdates';

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((done) => { resolve = done; });
  return { promise, resolve };
}

describe('пауза автоматических обновлений', () => {
  it('отменяет старый GET, выполняет recovery без публикации и объединяет события при возобновлении', async () => {
    const updates = createAutoUpdates();
    const late = deferred();
    const resume = deferred();
    const reads: string[] = [];
    const displayed: string[] = [];
    let oldSignal: AbortSignal | undefined;
    const reader = updates.createReader(async (signal, view) => {
      reads.push(view.kind);
      if (reads.length === 1) {
        oldSignal = signal;
        await late.promise;
        return view.apply(() => displayed.push('старый GET'));
      }
      if (view.kind === 'resume') await resume.promise;
      view.apply(() => displayed.push(view.kind));
    });

    reader.invalidate();
    updates.toggle();
    expect(oldSignal?.aborted).toBe(true);
    reader.invalidate();
    reader.invalidate();
    reader.recover();
    expect(reads).toEqual(['live', 'silent']);
    late.resolve();
    await late.promise;
    expect(displayed).toEqual([]);

    updates.toggle();
    reader.invalidate();
    reader.invalidate();
    expect(updates.getSnapshot().mode).toBe('resuming');
    resume.resolve();
    await vi.waitFor(() => expect(updates.getSnapshot().mode).toBe('live'));
    expect(reads).toEqual(['live', 'silent', 'resume', 'live']);
    expect(displayed).toEqual(['resume', 'live']);
    reader.dispose();
  });

  it('сохраняет весь снимок при ошибке ресурса и позволяет возобновить после ручного чтения', async () => {
    const updates = createAutoUpdates();
    const displayed: string[] = [];
    let fail = true;
    const reader = updates.createReader(async (_, view) => view.apply(() => displayed.push(view.kind)));
    const other = updates.createReader(async (_, view) => {
      if (fail) view.fail(new Error('test failure'), () => {});
    });
    updates.toggle();
    updates.toggle();
    await vi.waitFor(() => expect(updates.getSnapshot().mode).toBe('failed'));
    expect(displayed).toEqual([]);

    reader.refresh();
    await vi.waitFor(() => expect(displayed).toEqual(['explicit']));
    expect(updates.getSnapshot().mode).toBe('failed');
    fail = false;
    updates.toggle();
    await vi.waitFor(() => expect(updates.getSnapshot().mode).toBe('live'));
    expect(displayed).toEqual(['explicit', 'resume']);
    reader.dispose();
    other.dispose();
  });

  it('проверяет отказ в доступе на паузе и отклоняет снимок после смены области', async () => {
    const updates = createAutoUpdates();
    const denied = vi.fn();
    updates.setAccessCheck(denied);
    const late = deferred();
    const publish = vi.fn();
    const reader = updates.createReader(async (_, view) => {
      if (view.kind === 'silent') view.fail({ status: 401 }, publish);
      if (view.kind === 'resume') {
        await late.promise;
        view.apply(publish);
      }
    });
    updates.toggle();
    reader.recover();
    expect(denied).toHaveBeenCalledOnce();
    updates.toggle();
    reader.dispose();
    late.resolve();
    await late.promise;
    expect(publish).not.toHaveBeenCalled();
    expect(updates.getSnapshot().mode).toBe('paused');
  });
});
