import { Modal } from '@mantine/core';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { extendSession, fetchSessionTiming, type SessionTiming } from './authApi';
import { useAuth } from './authContext';

const WARNING_MS = 120_000;
const POLL_MS = 60_000;
const RETRY_MS = 5_000;
const REQUEST_MS = 8_000;
const CHANNEL = 'trade-bff-session-timing';
const SESSION_DIALOG_TRANSITION = {
  // Текст должен оставаться контрастным уже в момент установки фокуса.
  transitionProperty: 'transform',
  in: { transform: 'translateY(0)' },
  out: { transform: 'translateY(-30px)' },
};

type Deadline = { kind: 'unknown' | 'active' } | { kind: 'warning'; expiresAt: number };

export function SessionDeadline({ onLogin }: { onLogin: () => void }) {
  const { sessionExpired, captureTimingCheck, expireSession, restoreSession } = useAuth();
  const expired = sessionExpired === true;
  const [deadline, setDeadline] = useState<Deadline>({ kind: 'unknown' });
  const [dismissed, setDismissed] = useState<number>();
  const [extending, setExtending] = useState(false);
  const [extensionError, setExtensionError] = useState(false);
  const extend = useRef<() => void>(() => {});
  const reentryButton = useRef<HTMLButtonElement>(null);
  const wasWarningOpen = useRef(false);

  useEffect(() => {
    const lifetime = new AbortController();
    const channel = typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel(CHANNEL);
    let timer: number | undefined;
    let checking: AbortController | undefined;
    let actionPending = false;
    let lastTiming: { expiresAt: number; deadline: number } | undefined;

    async function apply(timing: SessionTiming | null, signal: AbortSignal) {
      if (timing === null) {
        if (!expired) expireSession();
        return;
      }
      if (expired) {
        setDeadline({ kind: 'unknown' });
        setDismissed(undefined);
        setExtending(false);
        setExtensionError(false);
        await restoreSession(signal);
        return;
      }

      lastTiming = { expiresAt: timing.expiresAt, deadline: performance.now() + timing.remainingMs };
      const warning = timing.remainingMs <= WARNING_MS;
      setDeadline(warning ? { kind: 'warning', expiresAt: timing.expiresAt } : { kind: 'active' });
      if (!warning) {
        setDismissed(undefined);
        setExtensionError(false);
      }
      const untilNextBoundary = warning ? timing.remainingMs : timing.remainingMs - WARNING_MS;
      timer = window.setTimeout(refresh, Math.max(1000, Math.min(POLL_MS, untilNextBoundary)));
    }

    function refresh() {
      if (lifetime.signal.aborted || actionPending || checking || document.visibilityState === 'hidden') return;
      window.clearTimeout(timer);
      const current = new AbortController();
      checking = current;
      const isCurrent = captureTimingCheck();
      const request = AbortSignal.any([current.signal, lifetime.signal, AbortSignal.timeout(REQUEST_MS)]);
      void fetchSessionTiming(request).then(
        (timing) => {
          if (request.aborted) return;
          if (isCurrent()) return apply(timing, request);
          timer = window.setTimeout(refresh, RETRY_MS);
        },
        () => {
          if (current.signal.aborted || lifetime.signal.aborted) return;
          if (lastTiming && lastTiming.deadline - performance.now() <= WARNING_MS) {
            setDeadline({ kind: 'warning', expiresAt: lastTiming.expiresAt });
          }
          timer = window.setTimeout(refresh, RETRY_MS);
        },
      ).finally(() => {
        if (checking === current) checking = undefined;
      });
    }

    extend.current = () => {
      if (expired || actionPending || lifetime.signal.aborted) return;
      actionPending = true;
      window.clearTimeout(timer);
      checking?.abort();
      checking = undefined;
      setExtending(true);
      setExtensionError(false);
      const isCurrent = captureTimingCheck();
      const request = AbortSignal.any([lifetime.signal, AbortSignal.timeout(REQUEST_MS)]);
      void extendSession(request).then(
        (timing) => {
          if (request.aborted) return;
          if (!isCurrent()) {
            timer = window.setTimeout(refresh, RETRY_MS);
            return;
          }
          void apply(timing, request);
          channel?.postMessage('refresh');
        },
        () => {
          if (lifetime.signal.aborted) return;
          setExtensionError(true);
          timer = window.setTimeout(refresh, RETRY_MS);
        },
      ).finally(() => {
        actionPending = false;
        if (!lifetime.signal.aborted) setExtending(false);
      });
    };

    if (channel) channel.onmessage = () => {
      checking?.abort();
      checking = undefined;
      refresh();
    };
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('focus', refresh);
    window.addEventListener('pageshow', refresh);
    window.addEventListener('online', refresh);
    refresh();
    return () => {
      lifetime.abort();
      checking?.abort();
      window.clearTimeout(timer);
      channel?.close();
      document.removeEventListener('visibilitychange', refresh);
      window.removeEventListener('focus', refresh);
      window.removeEventListener('pageshow', refresh);
      window.removeEventListener('online', refresh);
    };
  }, [captureTimingCheck, expireSession, expired, restoreSession]);

  const warning = !expired && deadline.kind === 'warning';
  const warningDismissed = warning && dismissed !== undefined && dismissed === deadline.expiresAt;
  const opened = expired || (warning && !warningDismissed);
  const dismiss = () => {
    if (warning && !extending) setDismissed(deadline.expiresAt);
  };
  useLayoutEffect(() => {
    const button = reentryButton.current;
    if (expired && wasWarningOpen.current && button?.isConnected && document.activeElement !== button) {
      button.focus({ preventScroll: true });
    }
    wasWarningOpen.current = warning && opened;
  }, [expired, warning, opened]);

  return (
    <>
      {warningDismissed && (
        <button type="button" className="button button-secondary" onClick={() => setDismissed(undefined)}>
          Сессия скоро завершится
        </button>
      )}
      <Modal.Root
        opened={opened}
        onClose={dismiss}
        closeOnClickOutside={false}
        closeOnEscape={!expired && !extending}
      >
        <Modal.Overlay />
        <Modal.Content transitionProps={{ transition: SESSION_DIALOG_TRANSITION }}>
          <Modal.Header>
            <Modal.Title>{expired ? 'Сессия завершена' : 'Сессия скоро завершится'}</Modal.Title>
          </Modal.Header>
          <Modal.Body>
            {expired ? 'Войдите снова, чтобы продолжить работу.' : 'Продлите сессию, чтобы сохранить доступ к приложению.'}
          </Modal.Body>
          <div className="session-deadline-actions">
            {!expired && extensionError && (
              <p className="alert" role="alert">Не удалось подтвердить продление. Повторите попытку.</p>
            )}
            <div className="form-actions">
              {expired ? (
                <button key="reentry" ref={reentryButton} type="button" className="button" data-autofocus onClick={onLogin}>Войти снова</button>
              ) : (
                <>
                  <button type="button" className="button" data-autofocus aria-disabled={extending || undefined} onClick={() => extend.current()}>
                    {extending ? 'Продлеваем…' : 'Продлить сессию'}
                  </button>
                  <button type="button" className="button button-secondary" disabled={extending} onClick={dismiss}>
                    Продолжить без продления
                  </button>
                </>
              )}
            </div>
          </div>
        </Modal.Content>
      </Modal.Root>
    </>
  );
}
