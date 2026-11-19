export interface InvalidationScheduler {
  invalidate: () => void;
  recover: () => void;
  cancelPending: () => void;
  dispose: () => void;
}

export function throwIfNetworkFailure(error: unknown, signal?: AbortSignal): void {
  if (error instanceof TypeError && signal?.aborted !== true) throw error;
}

// Обычные события объединяются; recovery освобождает ожидание отменённого GET.
export function createInvalidationScheduler(read: (signal: AbortSignal) => Promise<void>): InvalidationScheduler {
  let dirty = false;
  let running = false;
  let disposed = false;
  let active: AbortController | null = null;
  let recoveryPending = false;
  let waitingForOnline = false;

  function cancelOnlineWait() {
    if (waitingForOnline) window.removeEventListener('online', retryOnline);
    waitingForOnline = false;
  }

  function retryOnline() {
    cancelOnlineWait();
    if (!disposed) { dirty = true; void drain(); }
  }

  function cancelPending() {
    dirty = false;
    recoveryPending = false;
    cancelOnlineWait();
    active?.abort();
  }

  async function drain() {
    if (running || disposed) return;
    running = true;
    try {
      while (dirty && !disposed) {
        dirty = false;
        const isRecovery = recoveryPending;
        recoveryPending = false;
        active?.abort();
        const controller = new AbortController();
        active = controller;
        let onAbort: () => void = () => {};
        const aborted = new Promise<void>((resolve) => {
          onAbort = resolve;
          controller.signal.addEventListener('abort', onAbort, { once: true });
        });
        try {
          await Promise.race([(async () => read(controller.signal))(), aborted]);
        } catch (error) {
          if (isRecovery && !controller.signal.aborted && error instanceof TypeError && !disposed) {
            cancelOnlineWait();
            if (navigator.onLine) retryOnline();
            else {
              waitingForOnline = true;
              window.addEventListener('online', retryOnline, { once: true });
            }
          }
        } finally {
          controller.signal.removeEventListener('abort', onAbort);
        }
      }
    } finally {
      running = false;
      if (dirty && !disposed) void drain();
    }
  }

  return {
    invalidate() { if (!disposed) { dirty = true; void drain(); } },
    recover() { if (!disposed) { cancelOnlineWait(); recoveryPending = true; dirty = true; active?.abort(); void drain(); } },
    cancelPending,
    dispose() { disposed = true; cancelPending(); },
  };
}
