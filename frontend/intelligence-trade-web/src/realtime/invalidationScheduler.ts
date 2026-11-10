export interface InvalidationScheduler {
  invalidate: () => void;
  dispose: () => void;
}

// Одно чтение на ресурс: события во время GET объединяются в одно последующее чтение.
export function createInvalidationScheduler(read: () => Promise<void>): InvalidationScheduler {
  let dirty = false;
  let running = false;
  let disposed = false;

  async function drain() {
    if (running || disposed) return;
    running = true;
    try {
      while (dirty && !disposed) {
        dirty = false;
        await read();
      }
    } finally {
      running = false;
      if (dirty && !disposed) void drain();
    }
  }

  return {
    invalidate() { dirty = true; void drain(); },
    dispose() { disposed = true; dirty = false; },
  };
}
