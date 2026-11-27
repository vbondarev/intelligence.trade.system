import { createInvalidationScheduler, throwIfNetworkFailure, type InvalidationScheduler } from './invalidationScheduler';
import { HttpFailure } from '../errors/httpFailure';
import type { SessionReadContext } from '../auth/authTypes';

export interface ReadPresentation {
  kind: 'explicit' | 'live' | 'silent' | 'resume';
  beginRequest?: () => () => boolean;
  apply: (commit: () => void, isCurrent?: () => boolean) => void;
  fail: (error: unknown, commit: () => void, revoke?: () => void, isCurrent?: () => boolean) => void | 'stale-session';
}

type Read = (signal: AbortSignal, presentation: ReadPresentation) => Promise<void>;
export type ScreenReader = Omit<InvalidationScheduler, 'cancelPending' | 'dispose'> & {
  dispose: (reason?: 'access-revoked') => void;
  refresh: () => void;
  refreshAfterWrite: () => boolean;
  supersedeExplicitRead: () => void;
};
type EndSession = SessionReadContext['endSession'];
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

export function isStaleSessionAccessFailure(error: unknown, session?: SessionReadContext): boolean {
  return session?.isCurrent() === false && (statusOf(error) === 401 || resourceUnavailable(error));
}

export function createAutoUpdates(captureSessionRead?: () => SessionReadContext) {
  let snapshot = LIVE;
  let generation = 0;
  let resume: { controller: AbortController; participants: Map<ScreenReader, AbortController> } | null = null;
  const listeners = new Set<() => void>();
  const readers = new Map<ScreenReader, { read: Read; pause: () => void }>();
  const dirty = new Set<ScreenReader>();
  const isLive = () => snapshot.mode === 'live';
  let checkAccess: ((status: 401 | 403, traceId?: string | null) => void) | undefined;

  function reportAccessFailure(error: unknown, endSession?: EndSession) {
    const status = statusOf(error);
    if (status === 401 || (status === 403 && resourceUnavailable(error))) {
      const traceId = error instanceof HttpFailure ? error.traceId : null;
      if (status === 401 && endSession) endSession(traceId);
      else checkAccess?.(status, traceId);
    }
  }

  function handleAccess(error: unknown, revoke: (() => void) | undefined, endSession?: EndSession) {
    if (resourceUnavailable(error)) revoke?.();
    reportAccessFailure(error, endSession);
  }

  function setSnapshot(next: Snapshot) {
    snapshot = next;
    listeners.forEach((listener) => listener());
  }

  function cancelResume() {
    if (resume === null) return;
    resume.controller.abort();
    resume = null;
    setSnapshot({ ...snapshot, mode: 'paused', announcement: 'Автообновление приостановлено.' });
  }

  function failResume(controller: AbortController) {
    if (resume?.controller !== controller) return;
    resume = null;
    controller.abort();
    setSnapshot({ ...snapshot, mode: 'failed', announcement: RESUME_FAILURE });
  }

  function createReader(read: Read): ScreenReader {
    let explicitPending = false;
    let replacementExplicit = false;
    let activeRead: { explicit: boolean } | null = null;
    const live = createInvalidationScheduler((signal, hasRecoveryRetry) => {
      // Замена завершает явное чтение на паузе и подчиняется правилам ошибок recovery.
      const current = { explicit: explicitPending };
      const replacedExplicit = replacementExplicit;
      explicitPending = false;
      replacementExplicit = false;
      if (!current.explicit && !isLive()) {
        dirty.add(reader);
        return Promise.resolve();
      }
      activeRead = current;
      const session = captureSessionRead?.();
      const finish = () => { if (activeRead === current) activeRead = null; };
      const isExplicitCurrent = () => activeRead === current && current.explicit && !signal.aborted;
      if (current.explicit && !replacedExplicit) {
        return read(signal, {
          kind: 'explicit',
          apply: (commit, eligible) => { if (isExplicitCurrent() && eligible?.() !== false) commit(); },
          fail: (error, commit, revoke, eligible) => {
            if (!isExplicitCurrent() || eligible?.() === false) return;
            if (isStaleSessionAccessFailure(error, session)) return 'stale-session';
            handleAccess(error, revoke, session?.endSession);
            commit();
            throwIfNetworkFailure(error, signal);
          },
        }).finally(finish);
      }

      const version = generation;
      const isCurrent = replacedExplicit ? isExplicitCurrent
        : () => isLive() && generation === version && !signal.aborted;
      const apply: ReadPresentation['apply'] = (commit, eligible) => {
        if (isCurrent() && eligible?.() !== false) commit();
      };
      return read(signal, {
        kind: 'live',
        apply,
        fail: (error, commit, revoke, eligible) => {
          if (!isCurrent() || eligible?.() === false) return;
          if (isStaleSessionAccessFailure(error, session)) return 'stale-session';
          handleAccess(error, revoke, session?.endSession);
          if (!(error instanceof TypeError && hasRecoveryRetry())) apply(commit);
          throwIfNetworkFailure(error, signal);
        },
      }).finally(finish);
    }, () => activeRead?.explicit !== true, () => explicitPending && !replacementExplicit);
    const silent = createInvalidationScheduler((signal) => {
      const session = captureSessionRead?.();
      return read(signal, {
        kind: 'silent',
        apply: () => {},
        fail: (error, _, revoke, eligible) => {
          if (signal.aborted || eligible?.() === false) return;
          if (isStaleSessionAccessFailure(error, session)) return 'stale-session';
          handleAccess(error, revoke, session?.endSession);
          throwIfNetworkFailure(error, signal);
        },
      });
    });
    const reader: ScreenReader = {
      refresh() {
        cancelResume();
        explicitPending = true;
        replacementExplicit = false;
        live.invalidate();
      },
      refreshAfterWrite() {
        if (!readers.has(reader) || snapshot.mode === 'resuming') return false;
        live.cancelPending();
        explicitPending = true;
        replacementExplicit = false;
        live.invalidate();
        return true;
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
      dispose(reason) {
        readers.delete(reader);
        dirty.delete(reader);
        if (reason === 'access-revoked') resume?.participants.get(reader)?.abort();
        else cancelResume();
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
    const participants = new Map<ScreenReader, AbortController>();
    resume = { controller, participants };
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
      const refresh = Promise.all([...readers.entries()].map(async ([reader, { read }]) => {
        if (!readers.has(reader) || signal.aborted) return;
        const participant = new AbortController();
        participants.set(reader, participant);
        const readSignal = AbortSignal.any([signal, participant.signal]);
        let session = captureSessionRead?.();
        const captureEligibility = (eligible?: () => boolean) => {
          const requestSession = session;
          return () => !readSignal.aborted && requestSession?.isCurrent() !== false && eligible?.() !== false;
        };
        let finishRead = () => {};
        const abandoned = new Promise<void>((resolve) => {
          finishRead = resolve;
          readSignal.addEventListener('abort', finishRead, { once: true });
        });
        try {
          await Promise.race([read(readSignal, {
            kind: 'resume',
            beginRequest() {
              // Ожидание ручной страницы предшествует первому GET этого чтения.
              session = captureSessionRead?.();
              return session?.isCurrent ?? (() => true);
            },
            apply: (commit, eligible) => {
              const current = captureEligibility(eligible);
              if (current()) writes.push(() => { if (current()) commit(); });
            },
            fail: (error, commit, revoke, eligible) => {
              const current = captureEligibility(eligible);
              if (!current()) return;
              handleAccess(error, revoke, session?.endSession);
              if (statusOf(error) !== 404) throw error;
              if (!revoke) writes.push(() => { if (current()) commit(); });
            },
          }), abandoned]);
        } finally {
          readSignal.removeEventListener('abort', finishRead);
        }
      }));
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
