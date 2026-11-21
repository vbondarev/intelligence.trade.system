import { createInvalidationScheduler, throwIfNetworkFailure, type InvalidationScheduler } from './invalidationScheduler';
import { HttpFailure } from '../errors/httpFailure';

export interface ReadPresentation {
  kind: 'explicit' | 'live' | 'silent' | 'resume';
  apply: (commit: () => void, isCurrent?: () => boolean) => void;
  fail: (error: unknown, commit: () => void, revoke?: () => void, isCurrent?: () => boolean) => void;
}

type Read = (signal: AbortSignal, presentation: ReadPresentation) => Promise<void>;
export type ScreenReader = Omit<InvalidationScheduler, 'cancelPending'> & {
  refresh: () => void;
  supersedeExplicitRead: () => void;
};
type Mode = 'live' | 'paused' | 'resuming' | 'failed';
type Snapshot = { mode: Mode; pausedAt: number | null; announcement: string };

const LIVE: Snapshot = { mode: 'live', pausedAt: null, announcement: '' };
const RESUME_TIMEOUT_MS = 20_000;
const RESUME_FAILURE = 'Не удалось возобновить автообновление. Повторите попытку.';
export const explicitPresentation: ReadPresentation = {
  kind: 'explicit',
  apply: (commit, isCurrent) => { if (isCurrent?.() !== false) commit(); },
  fail: (error, commit, revoke, isCurrent) => {
    if (isCurrent?.() === false) return;
    if (resourceUnavailable(error)) revoke?.();
    commit();
  },
};

function statusOf(error: unknown) {
  return typeof error === 'object' && error !== null && 'status' in error ? error.status : undefined;
}

export function resourceUnavailable(error: unknown): boolean {
  const status = statusOf(error);
  const code = typeof error === 'object' && error !== null && 'code' in error ? error.code : undefined;
  return status === 404 || (status === 403 && code !== 'exchange_permissions_rejected');
}

export function createAutoUpdates() {
  let snapshot = LIVE;
  let generation = 0;
  let resume: AbortController | null = null;
  const listeners = new Set<() => void>();
  const readers = new Map<ScreenReader, { read: Read; pause: () => void }>();
  const dirty = new Set<ScreenReader>();
  const isLive = () => snapshot.mode === 'live';
  let checkAccess: ((status: 401 | 403, traceId?: string | null) => void) | undefined;

  function reportAccessFailure(error: unknown) {
    const status = statusOf(error);
    if (status === 401 || (status === 403 && resourceUnavailable(error))) {
      checkAccess?.(status, error instanceof HttpFailure ? error.traceId : null);
    }
  }

  function handleAccess(error: unknown, revoke?: () => void) {
    if (resourceUnavailable(error)) revoke?.();
    reportAccessFailure(error);
  }

  function setSnapshot(next: Snapshot) {
    snapshot = next;
    listeners.forEach((listener) => listener());
  }

  function cancelResume() {
    if (resume === null) return;
    resume.abort();
    resume = null;
    setSnapshot({ ...snapshot, mode: 'paused', announcement: 'Автообновление приостановлено.' });
  }

  function failResume(controller: AbortController) {
    resume = null;
    controller.abort();
    setSnapshot({ ...snapshot, mode: 'failed', announcement: RESUME_FAILURE });
  }

  function createReader(read: Read): ScreenReader {
    let explicitPending = false;
    let replacementExplicit = false;
    let activeRead: { explicit: boolean } | null = null;
    const live = createInvalidationScheduler((signal, hasRecoveryRetry) => {
      const current = { explicit: explicitPending };
      explicitPending = false;
      replacementExplicit = false;
      if (!current.explicit && !isLive()) {
        dirty.add(reader);
        return Promise.resolve();
      }
      activeRead = current;
      const finish = () => { if (activeRead === current) activeRead = null; };
      if (current.explicit) {
        const isCurrent = () => activeRead === current && current.explicit && !signal.aborted;
        return read(signal, {
          kind: 'explicit',
          apply: (commit, eligible) => { if (isCurrent() && eligible?.() !== false) commit(); },
          fail: (error, commit, revoke, eligible) => {
            if (!isCurrent() || eligible?.() === false) return;
            handleAccess(error, revoke);
            commit();
            throwIfNetworkFailure(error, signal);
          },
        }).finally(finish);
      }

      const version = generation;
      const isCurrent = () => isLive() && generation === version && !signal.aborted;
      const apply: ReadPresentation['apply'] = (commit, eligible) => {
        if (isCurrent() && eligible?.() !== false) commit();
      };
      return read(signal, {
        kind: 'live',
        apply,
        fail: (error, commit, revoke, eligible) => {
          if (!isCurrent() || eligible?.() === false) return;
          handleAccess(error, revoke);
          if (!(error instanceof TypeError && hasRecoveryRetry())) apply(commit);
          throwIfNetworkFailure(error, signal);
        },
      }).finally(finish);
    }, () => activeRead?.explicit !== true, () => explicitPending && !replacementExplicit);
    const silent = createInvalidationScheduler((signal) => read(signal, {
      kind: 'silent',
      apply: () => {},
      fail: (error, _, revoke, eligible) => {
        if (signal.aborted || eligible?.() === false) return;
        handleAccess(error, revoke);
        throwIfNetworkFailure(error, signal);
      },
    }));
    const reader: ScreenReader = {
      refresh() {
        cancelResume();
        explicitPending = true;
        replacementExplicit = false;
        live.invalidate();
      },
      supersedeExplicitRead() {
        explicitPending = false;
        replacementExplicit = false;
        if (activeRead) activeRead.explicit = false;
      },
      invalidate() {
        if (isLive()) live.invalidate();
        else dirty.add(reader);
      },
      recover() {
        if (isLive()) {
          if (activeRead?.explicit) {
            explicitPending = true;
            replacementExplicit = true;
          }
          live.recover();
        } else {
          dirty.add(reader);
          silent.recover();
        }
      },
      dispose() {
        readers.delete(reader);
        dirty.delete(reader);
        cancelResume();
        live.dispose();
        silent.dispose();
      },
    };
    cancelResume();
    readers.set(reader, { read, pause: () => { if (!activeRead?.explicit) live.cancelPending(); } });
    return reader;
  }

  async function resumeUpdates() {
    if (resume !== null) return;
    const controller = new AbortController();
    resume = controller;
    const { signal } = controller;
    dirty.clear();
    setSnapshot({ ...snapshot, mode: 'resuming', announcement: '' });
    const writes: (() => void)[] = [];
    let finishOnAbort = () => {};
    const cancelled = new Promise<void>((resolve) => {
      finishOnAbort = resolve;
      signal.addEventListener('abort', finishOnAbort, { once: true });
    });
    const timeout = setTimeout(() => failResume(controller), RESUME_TIMEOUT_MS);
    try {
      const refresh = Promise.all([...readers.values()].map(({ read }) => read(signal, {
        kind: 'resume',
        apply: (commit, eligible) => {
          if (!signal.aborted && eligible?.() !== false) {
            writes.push(() => { if (eligible?.() !== false) commit(); });
          }
        },
        fail: (error, commit, revoke, eligible) => {
          if (signal.aborted || eligible?.() === false) return;
          const status = statusOf(error);
          handleAccess(error, revoke);
          if (status === 404) {
            if (!revoke) writes.push(() => { if (eligible?.() !== false) commit(); });
          }
          else throw error;
        },
      })));
      await Promise.race([refresh, cancelled]);
      if (signal.aborted) return;
      for (const commit of writes) commit();
      resume = null;
      setSnapshot({ ...LIVE, announcement: 'Автообновление возобновлено.' });
      const pending = [...dirty];
      dirty.clear();
      pending.forEach((reader) => reader.invalidate());
    } catch {
      if (signal.aborted) return;
      failResume(controller);
    } finally {
      clearTimeout(timeout);
      signal.removeEventListener('abort', finishOnAbort);
    }
  }

  return {
    getSnapshot: () => snapshot,
    subscribe(listener: () => void) {
      listeners.add(listener);
      return () => { listeners.delete(listener); };
    },
    isLive,
    setAccessCheck(check?: typeof checkAccess) { checkAccess = check; },
    reportAccessFailure,
    createReader,
    toggle() {
      if (isLive()) {
        generation += 1;
        setSnapshot({ mode: 'paused', pausedAt: Date.now(), announcement: 'Автообновление приостановлено.' });
        readers.forEach(({ pause }) => pause());
      } else {
        void resumeUpdates();
      }
    },
    cancelResume,
  };
}
