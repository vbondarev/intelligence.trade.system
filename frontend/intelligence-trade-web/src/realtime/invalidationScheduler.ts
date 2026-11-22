export interface InvalidationScheduler {
  invalidate: () => void;
  recover: () => void;
  cancelPending: () => void;
  dispose: () => void;
}

export function throwIfNetworkFailure(error: unknown, signal?: AbortSignal): void {
  if (error instanceof TypeError && signal?.aborted !== true) throw error;
}

const RECOVERY_RETRY_DELAYS_MS = [250, 500];

// Обычные события объединяются; recovery освобождает ожидание отменённого GET.
export function createInvalidationScheduler(
  read: (signal: AbortSignal, hasRecoveryRetry: () => boolean) => Promise<void>,
  canInterruptForRetry: () => boolean = () => true,
  hasExplicitRead: () => boolean = () => false,
): InvalidationScheduler {
  let dirty = false;
  let running = false;
  let disposed = false;
  let active: AbortController | null = null;
  let recoveryPending = false;
  let recovery: { retries: number } | null = null;
  let retryTimer: ReturnType<typeof setTimeout> | undefined;
  let waitingForOnline = false;

  function cancelOnlineWait() {
    if (waitingForOnline) window.removeEventListener('online', retryOnline);
    waitingForOnline = false;
  }

  function clearRecovery() {
    recovery = null;
    recoveryPending = false;
    clearTimeout(retryTimer);
    retryTimer = undefined;
    cancelOnlineWait();
  }

  function retryOnline() {
    cancelOnlineWait();
    scheduleRecoveryRetry();
  }

  function scheduleRecoveryRetry() {
    const cycle = recovery;
    if (!cycle) return;
    const delay = RECOVERY_RETRY_DELAYS_MS[cycle.retries];
    if (delay === undefined) {
      clearRecovery();
      return;
    }
    if (!navigator.onLine) {
      if (!waitingForOnline) {
        waitingForOnline = true;
        window.addEventListener('online', retryOnline, { once: true });
      }
      return;
    }
    if (retryTimer !== undefined) return;
    retryTimer = setTimeout(() => {
      retryTimer = undefined;
      if (recovery !== cycle) return;
      if (!navigator.onLine) {
        scheduleRecoveryRetry();
        return;
      }
      cycle.retries += 1;
      recoveryPending = true;
      dirty = true;
      // Явное чтение сохраняет право завершиться. Фоновый GET не удерживает очередь recovery.
      if (canInterruptForRetry()) active?.abort();
      void drain();
    }, delay);
  }

  function cancelPending() {
    dirty = false;
    clearRecovery();
    active?.abort();
  }

  function canDrain() {
    return dirty && !disposed && (hasExplicitRead() || recoveryPending || (!waitingForOnline && retryTimer === undefined));
  }

  async function drain() {
    if (running || disposed) return;
    running = true;
    try {
      while (canDrain()) {
        const explicit = hasExplicitRead();
        const cycle = recoveryPending && !explicit ? recovery : null;
        if (!explicit) recoveryPending = false;
        dirty = recoveryPending;
        active?.abort();
        const controller = new AbortController();
        active = controller;
        let onAbort: () => void = () => {};
        const aborted = new Promise<void>((resolve) => {
          onAbort = resolve;
          controller.signal.addEventListener('abort', onAbort, { once: true });
        });
        try {
          const hasRecoveryRetry = () => cycle !== null && recovery === cycle && !controller.signal.aborted
            && RECOVERY_RETRY_DELAYS_MS[cycle.retries] !== undefined;
          await Promise.race([(async () => read(controller.signal, hasRecoveryRetry))(), aborted]);
          if (cycle && recovery === cycle && !controller.signal.aborted) clearRecovery();
        } catch (error) {
          if (cycle && recovery === cycle && !controller.signal.aborted) {
            if (error instanceof TypeError) scheduleRecoveryRetry();
            else clearRecovery();
          }
        } finally {
          controller.signal.removeEventListener('abort', onAbort);
          active = null;
        }
      }
    } finally {
      running = false;
      if (canDrain()) void drain();
    }
  }

  return {
    invalidate() { if (!disposed) { dirty = true; void drain(); } },
    recover() {
      if (disposed) return;
      clearRecovery();
      recovery = { retries: 0 };
      recoveryPending = true;
      dirty = true;
      active?.abort();
      void drain();
    },
    cancelPending,
    dispose() { disposed = true; cancelPending(); },
  };
}
