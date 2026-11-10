export interface InvalidationScheduler {
  invalidate: () => void;
  recover: () => void;
  dispose: () => void;
}

// РћР±С‹С‡РЅС‹Рµ СЃРѕР±С‹С‚РёСЏ РѕР±СЉРµРґРёРЅСЏСЋС‚СЃСЏ; recovery РѕСЃРІРѕР±РѕР¶РґР°РµС‚ РѕР¶РёРґР°РЅРёРµ РѕС‚РјРµРЅС‘РЅРЅРѕРіРѕ GET.
export function createInvalidationScheduler(read: (signal: AbortSignal) => Promise<void>): InvalidationScheduler {
  let dirty = false;
  let running = false;
  let disposed = false;
  let active: AbortController | null = null;

  async function drain() {
    if (running || disposed) return;
    running = true;
    try {
      while (dirty && !disposed) {
        dirty = false;
        active?.abort();
        const controller = new AbortController();
        active = controller;
        let onAbort: () => void = () => {};
        const aborted = new Promise<void>((resolve) => {
          onAbort = resolve;
          controller.signal.addEventListener('abort', onAbort, { once: true });
        });
        try {
          await Promise.race([(async () => read(controller.signal))().catch(() => {}), aborted]);
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
    recover() { if (!disposed) { dirty = true; active?.abort(); void drain(); } },
    dispose() { disposed = true; dirty = false; active?.abort(); },
  };
}
