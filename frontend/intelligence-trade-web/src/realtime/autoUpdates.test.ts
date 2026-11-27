import { describe, expect, it, vi } from 'vitest';
import { HttpFailure, readSuccessJson } from '../errors/httpFailure';
import { createAutoUpdates, type ReadPresentation } from './autoUpdates';

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((done) => { resolve = done; });
  return { promise, resolve };
}

describe('пауза автоматических обновлений', () => {
  it('откладывает подтверждающий GET во время resume и заменяет активное чтение на паузе', async () => {
    const updates = createAutoUpdates();
    const waitForRead = deferred();
    const pendingResume = deferred();
    const results: string[] = [];
    const signals: AbortSignal[] = [];
    const reader = updates.createReader(async (signal, view) => {
      signals.push(signal);
      if (signals.length === 1) await waitForRead.promise;
      if (view.kind === 'resume') await pendingResume.promise;
      view.apply(() => results.push(view.kind));
    });

    updates.toggle();
    reader.refresh();
    expect(reader.refreshAfterWrite()).toBe(true);
    expect(signals[0]?.aborted).toBe(true);
    await vi.waitFor(() => expect(results).toEqual(['explicit']));
    expect(updates.getSnapshot().mode).toBe('paused');

    updates.toggle();
    expect(reader.refreshAfterWrite()).toBe(false);
    expect(updates.getSnapshot().mode).toBe('resuming');
    pendingResume.resolve();
    await vi.waitFor(() => expect(updates.getSnapshot().mode).toBe('live'));
    waitForRead.resolve();
    expect(results).toEqual(['explicit', 'resume']);
    reader.dispose();
    expect(reader.refreshAfterWrite()).toBe(false);
  });

  it.each([true, false])('объединяет события во время ожидания повтора (online=%s)', async (initiallyOnline) => {
    vi.useFakeTimers();
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(initiallyOnline);
    const updates = createAutoUpdates();
    const warning = vi.fn();
    const read = vi.fn(async (_: AbortSignal, view: Parameters<Parameters<typeof updates.createReader>[0]>[1]) => {
      view.fail(new TypeError('network'), warning);
    });
    const reader = updates.createReader(read);
    try {
      reader.recover();
      await vi.advanceTimersByTimeAsync(0);
      reader.invalidate();
      reader.invalidate();
      if (!initiallyOnline) {
        await vi.advanceTimersByTimeAsync(1000);
        expect(read).toHaveBeenCalledOnce();
        online.mockReturnValue(true);
        window.dispatchEvent(new Event('online'));
      }
      await vi.advanceTimersByTimeAsync(249);
      expect(read).toHaveBeenCalledOnce();
      expect(warning).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(1);
      expect(read).toHaveBeenCalledTimes(2);
      expect(warning).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(500);
      expect(read).toHaveBeenCalledTimes(3);
      expect(warning).toHaveBeenCalledOnce();
    } finally {
      reader.dispose();
      online.mockRestore();
      vi.useRealTimers();
    }
  });

  it('отбрасывает подготовленный снимок зависимой области после отзыва доступа', async () => {
    const updates = createAutoUpdates();
    let generation = 0;
    const dependent = vi.fn();
    const independent = vi.fn();
    const hide = vi.fn();
    const readers = [
      updates.createReader(async (_, view) => {
        const expected = generation;
        view.apply(dependent, () => generation === expected);
      }),
      updates.createReader(async (_, view) => {
        view.fail({ status: 404 }, () => {}, () => {
          generation += 1;
          hide();
        });
      }),
      updates.createReader(async (_, view) => view.apply(independent)),
    ];
    updates.toggle();
    updates.toggle();
    await vi.waitFor(() => expect(updates.getSnapshot().mode).toBe('live'));
    expect(hide).toHaveBeenCalledOnce();
    expect(dependent).not.toHaveBeenCalled();
    expect(independent).toHaveBeenCalledOnce();
    readers.forEach((reader) => reader.dispose());
  });

  it.each([false, true])('проверяет эпоху результата resume (смена после подготовки: %s)', async (prepared) => {
    let epoch = 0;
    const updates = createAutoUpdates(() => {
      const captured = epoch;
      return { isCurrent: () => captured === epoch, endSession: vi.fn() };
    });
    const read = deferred();
    const release = deferred();
    const expired = vi.fn();
    const accepted = vi.fn();
    updates.createReader(async (_, view) => {
      if (!prepared) await read.promise;
      view.apply(expired);
    });
    updates.createReader(async (_, view) => {
      await release.promise;
      view.beginRequest?.();
      view.apply(accepted);
    });

    updates.toggle();
    updates.toggle();
    epoch += 1;
    read.resolve();
    release.resolve();

    await vi.waitFor(() => expect(updates.getSnapshot().mode).toBe('live'));
    expect(expired).not.toHaveBeenCalled();
    expect(accepted).toHaveBeenCalledOnce();
  });

  it.each([403, 404, 503])('игнорирует устаревший %s во время resume', async (status) => {
    let epoch = 0;
    const updates = createAutoUpdates(() => {
      const captured = epoch;
      return { isCurrent: () => captured === epoch, endSession: vi.fn() };
    });
    const accessCheck = vi.fn();
    const hide = vi.fn();
    const notify = vi.fn();
    const pending = deferred();
    updates.setAccessCheck(accessCheck);
    updates.createReader(async (_, view) => {
      await pending.promise;
      view.fail({ status }, notify, hide);
    });

    updates.toggle();
    updates.toggle();
    epoch += 1;
    pending.resolve();

    await vi.waitFor(() => expect(updates.getSnapshot().mode).toBe('live'));
    expect(hide).not.toHaveBeenCalled();
    expect(accessCheck).not.toHaveBeenCalled();
    expect(notify).not.toHaveBeenCalled();
  });

  describe.each(['explicit', 'live', 'silent'] as const)('эпоха отказа в режиме %s', (mode) => {
    it.each([401, 403, 404, 503, 'bybit'] as const)('проверяет актуальность ответа %s', async (outcome) => {
      for (const stale of [false, true]) {
        let epoch = 0;
        const endSession = vi.fn();
        const updates = createAutoUpdates(() => {
          const captured = epoch;
          return { isCurrent: () => captured === epoch, endSession };
        });
        const pending = deferred();
        const finished = deferred();
        const checkAccess = vi.fn();
        const revoke = vi.fn();
        const publish = vi.fn();
        const status = outcome === 'bybit' ? 403 : outcome;
        const error = new HttpFailure('Отказ', status, outcome === 'bybit' ? 'exchange_permissions_rejected' : null, 'trace');
        updates.setAccessCheck(checkAccess);
        const read = vi.fn(async (_: AbortSignal, view: ReadPresentation) => {
          await pending.promise;
          view.fail(error, publish, revoke);
          finished.resolve();
        });
        const reader = updates.createReader(read);
        if (mode === 'silent') updates.toggle();
        if (mode === 'explicit') reader.refresh();
        else reader.recover();
        expect(read).toHaveBeenCalledOnce();
        if (stale) epoch += 1;
        pending.resolve();
        await finished.promise;
        const accepted = !stale || outcome === 'bybit' || outcome === 503;
        expect(revoke).toHaveBeenCalledTimes(accepted && (outcome === 403 || outcome === 404) ? 1 : 0);
        expect(checkAccess).toHaveBeenCalledTimes(accepted && outcome === 403 ? 1 : 0);
        expect(endSession).toHaveBeenCalledTimes(accepted && outcome === 401 ? 1 : 0);
        expect(publish).toHaveBeenCalledTimes(accepted && mode !== 'silent' ? 1 : 0);
        expect(read).toHaveBeenCalledOnce();
        reader.dispose();
      }
    });
  });

  it('завершает resume после удаления зависимого reader с незавершённым GET', async () => {
    const updates = createAutoUpdates();
    const remaining = deferred();
    const revoke = deferred();
    const never = deferred();
    const publish = vi.fn();
    let dependentSignal: AbortSignal | undefined;
    const dependent = updates.createReader(async (signal) => {
      dependentSignal = signal;
      await never.promise;
    });
    const primary = updates.createReader(async (_, view) => {
      await revoke.promise;
      view.fail({ status: 404 }, () => {}, () => dependent.dispose('access-revoked'));
    });
    const independent = updates.createReader(async (_, view) => {
      await remaining.promise;
      view.apply(publish);
    });
    try {
      updates.toggle();
      updates.toggle();
      revoke.resolve();
      await vi.waitFor(() => expect(dependentSignal?.aborted).toBe(true));
      expect(updates.getSnapshot().mode).toBe('resuming');
      remaining.resolve();
      await vi.waitFor(() => expect(updates.getSnapshot().mode).toBe('live'));
      expect(publish).toHaveBeenCalledOnce();
    } finally {
      never.resolve();
      remaining.resolve();
      primary.dispose();
      independent.dispose();
    }
  });

  it('повторяет GET после сбоя потока успешного HTTP-ответа', async () => {
    vi.useFakeTimers();
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true);
    const updates = createAutoUpdates();
    const notice = vi.fn();
    const publish = vi.fn();
    let attempts = 0;
    const reader = updates.createReader(async (signal, view) => {
      const response = ++attempts === 1
        ? new Response(new ReadableStream({
          start(controller) { controller.error(new TypeError('Сбой сети')); },
        }))
        : new Response('{}');
      try {
        await readSuccessJson(response, () => new HttpFailure('Некорректный JSON', 200), signal);
        view.apply(publish);
      } catch (error) {
        view.fail(error, notice);
      }
    });
    try {
      reader.recover();
      await vi.advanceTimersByTimeAsync(249);
      expect(attempts).toBe(1);
      expect(notice).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(1);
      expect(attempts).toBe(2);
      expect(publish).toHaveBeenCalledOnce();
      expect(notice).not.toHaveBeenCalled();
    } finally {
      reader.dispose();
      online.mockRestore();
      vi.useRealTimers();
    }
  });

  it.each([false, true])('публикует только терминальный исход замены ручного чтения (исчерпание: %s)', async (exhausted) => {
    vi.useFakeTimers();
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true);
    const updates = createAutoUpdates();
    const oldRead = deferred();
    const warn = vi.fn();
    const publish = vi.fn();
    let calls = 0;
    const reader = updates.createReader(async (_, view) => {
      calls += 1;
      if (calls === 1) return oldRead.promise;
      if (calls === 2 || exhausted) view.fail(new TypeError('Сбой сети'), warn);
      else view.apply(publish);
    });
    try {
      reader.refresh();
      reader.recover();
      await vi.advanceTimersByTimeAsync(249);
      expect(calls).toBe(2);
      expect(warn).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(exhausted ? 501 : 1);
      expect(calls).toBe(exhausted ? 4 : 3);
      expect(warn).toHaveBeenCalledTimes(exhausted ? 1 : 0);
      expect(publish).toHaveBeenCalledTimes(exhausted ? 0 : 1);
    } finally {
      reader.dispose();
      oldRead.resolve();
      online.mockRestore();
      vi.useRealTimers();
    }
  });

  it.each([false, true])('сохраняет первое чтение при паузе после reconnect (замена запущена: %s)', async (started) => {
    const updates = createAutoUpdates();
    const pending = deferred();
    const replacement = deferred();
    const publish = vi.fn();
    let calls = 0;
    const reader = updates.createReader(async (_, view) => {
      if (++calls === 1) return pending.promise;
      await replacement.promise;
      view.apply(publish);
    });
    try {
      reader.refresh();
      reader.recover();
      if (started) await vi.waitFor(() => expect(calls).toBe(2));
      updates.toggle();
      replacement.resolve();
      await vi.waitFor(() => expect(publish).toHaveBeenCalledOnce());
      expect(calls).toBe(2);
      expect(updates.getSnapshot().mode).toBe('paused');
    } finally {
      pending.resolve();
      replacement.resolve();
      reader.dispose();
    }
  });

  it('таймер повтора ждёт явное чтение, сохраняя его право публикации', async () => {
    vi.useFakeTimers();
    const online = vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true);
    const updates = createAutoUpdates();
    const explicit = deferred();
    const reads: string[] = [];
    const publish = vi.fn();
    let explicitSignal: AbortSignal | undefined;
    const reader = updates.createReader(async (signal, view) => {
      reads.push(view.kind);
      if (reads.length === 1) {
        view.fail(new TypeError('network'), () => {});
        return;
      }
      if (view.kind === 'explicit') {
        explicitSignal = signal;
        await explicit.promise;
      }
      view.apply(() => publish(view.kind));
    });
    try {
      reader.recover();
      await vi.advanceTimersByTimeAsync(10);
      reader.refresh();
      await vi.advanceTimersByTimeAsync(240);
      expect(reads).toEqual(['live', 'explicit']);
      expect(explicitSignal?.aborted).toBe(false);
      explicit.resolve();
      await vi.advanceTimersByTimeAsync(0);
      expect(reads).toEqual(['live', 'explicit', 'live']);
      expect(publish.mock.calls).toEqual([['explicit'], ['live']]);
    } finally {
      explicit.resolve();
      reader.dispose();
      online.mockRestore();
      vi.useRealTimers();
    }
  });

  it('отзывает явное чтение и его уже запланированное продолжение', async () => {
    const updates = createAutoUpdates();
    const previous = deferred();
    const reads: string[] = [];
    const publish = vi.fn();
    const reader = updates.createReader(async (_, view) => {
      reads.push(view.kind);
      if (reads.length > 1) return;
      await previous.promise;
      view.apply(publish);
      expect(() => view.fail(new TypeError('поздний сетевой отказ'), publish)).not.toThrow();
    });

    reader.refresh();
    reader.refresh();
    reader.supersedeExplicitRead();
    reader.recover();
    await vi.waitFor(() => expect(reads).toEqual(['explicit', 'live']));
    previous.resolve();
    await previous.promise;
    expect(publish).not.toHaveBeenCalled();
    reader.dispose();
  });

  it('позволяет актуальному явному чтению завершиться на паузе', async () => {
    const updates = createAutoUpdates();
    const response = deferred();
    const publish = vi.fn();
    const reader = updates.createReader(async (_, view) => {
      await response.promise;
      view.apply(publish);
    });

    reader.refresh();
    updates.toggle();
    response.resolve();
    await vi.waitFor(() => expect(publish).toHaveBeenCalledOnce());
    expect(updates.getSnapshot().mode).toBe('paused');
    reader.dispose();
  });

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
