import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AuthApiError, fetchSession, logout as logoutRequest, startLogin } from './authApi';
import { AuthContext } from './authContext';
import type { AuthContextValue, AuthState, BrowserSession, SessionReadContext } from './authTypes';

const LOADING_STATE: AuthState = {
  loading: true,
  authenticated: false,
  user: null,
  error: null,
  logoutAvailable: false,
};
const SESSION_ERROR = 'Не удалось проверить сессию. Попробуйте ещё раз.';
const LOGOUT_ERROR = 'Не удалось выйти. Попробуйте ещё раз.';

function toAuthState(session: BrowserSession): AuthState {
  return session.authenticated
    ? { loading: false, authenticated: true, user: session.user, error: null, logoutAvailable: false }
    : { loading: false, authenticated: false, user: null, error: null, logoutAvailable: false };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState & { sessionInstance: number }>({ ...LOADING_STATE, sessionInstance: 0 });
  const [attempt, setAttempt] = useState(0);
  const epoch = useRef(0);
  const sessionGeneration = useRef(0);
  const currentUserId = useRef<string | null>(null);
  const expired = useRef(false);
  const logoutPending = useRef(false);

  const replaceSession = useCallback((next: AuthState) => {
    epoch.current += 1;
    sessionGeneration.current += 1;
    currentUserId.current = next.user?.userId ?? null;
    expired.current = next.sessionExpired === true;
    setState((current) => ({
      ...next,
      sessionInstance: next.authenticated ? current.sessionInstance + 1 : current.sessionInstance,
    }));
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    const requestedEpoch = ++epoch.current;
    fetchSession(controller.signal).then(
      (session) => {
        if (!controller.signal.aborted && epoch.current === requestedEpoch) {
          replaceSession(toAuthState(session));
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted && epoch.current === requestedEpoch) {
          const status = error instanceof AuthApiError ? error.status : 0;
          replaceSession({
            ...toAuthState({ authenticated: false }),
            error: SESSION_ERROR,
            // 403 и 503 сохраняют server-side session, поэтому full logout должен оставаться доступен.
            logoutAvailable: status === 403 || status === 503,
          });
        }
      },
    );
    return () => controller.abort();
  }, [attempt, replaceSession]);

  const retry = useCallback(() => {
    replaceSession(LOADING_STATE);
    setAttempt((current) => current + 1);
  }, [replaceSession]);

  const expireSession = useCallback(() => {
    if (expired.current || logoutPending.current || currentUserId.current === null) return;
    replaceSession({ ...toAuthState({ authenticated: false }), sessionExpired: true });
  }, [replaceSession]);

  const revalidate = useCallback(async (expectedUserId: string, signal: AbortSignal) => {
    const canReconnect = () => !signal.aborted && !logoutPending.current && currentUserId.current === expectedUserId;
    if (!canReconnect()) return false;

    const requestedEpoch = ++epoch.current;
    try {
      const session = await fetchSession(signal);
      if (!canReconnect() || epoch.current !== requestedEpoch) return canReconnect();
      if (!session.authenticated) {
        expireSession();
      } else if (session.user.userId !== expectedUserId) {
        replaceSession(toAuthState(session));
      } else {
        // Подтверждение сессии отзывает и право бизнес-чтений, начатых во время проверки.
        epoch.current += 1;
      }
      return canReconnect();
    } catch (error) {
      if (!canReconnect() || epoch.current !== requestedEpoch) return canReconnect();
      if (error instanceof AuthApiError && error.status === 403) {
        replaceSession({ ...toAuthState({ authenticated: false }), error: SESSION_ERROR, logoutAvailable: true });
        return false;
      }
      return true;
    }
  }, [expireSession, replaceSession]);

  const logout = useCallback(async () => {
    if (logoutPending.current) return;
    logoutPending.current = true;
    sessionGeneration.current += 1;
    const requestedEpoch = ++epoch.current;
    try {
      await logoutRequest();
    } catch {
      logoutPending.current = false;
      if (epoch.current !== requestedEpoch) return;
      setState((current) => ({ ...current, error: LOGOUT_ERROR }));
      const userId = currentUserId.current;
      if (userId !== null) await revalidate(userId, AbortSignal.timeout(8000));
    }
  }, [revalidate]);

  const captureTimingCheck = useCallback(() => {
    // Успешная realtime-перепроверка сохраняет поколение browser session.
    const generation = sessionGeneration.current;
    return () => sessionGeneration.current === generation && !logoutPending.current;
  }, []);

  const restoreSession = useCallback(async (signal: AbortSignal) => {
    if (!expired.current || signal.aborted) return;
    const requestedEpoch = epoch.current;
    try {
      const session = await fetchSession(signal);
      if (!signal.aborted && epoch.current === requestedEpoch && session.authenticated) {
        replaceSession(toAuthState(session));
      }
    } catch (error) {
      if (!signal.aborted && epoch.current === requestedEpoch && error instanceof AuthApiError && error.status === 403) {
        replaceSession({ ...toAuthState({ authenticated: false }), error: SESSION_ERROR, logoutAvailable: true });
      }
    }
  }, [replaceSession]);

  const captureSessionRead = useCallback((): SessionReadContext => {
    const requestedEpoch = epoch.current;
    const isCurrent = () => epoch.current === requestedEpoch;
    return {
      isCurrent,
      endSession(traceId) {
        if (!isCurrent() || expired.current || logoutPending.current) return;
        replaceSession({ ...toAuthState({ authenticated: false }), sessionEnded: true, sessionTraceId: traceId });
      },
    };
  }, [replaceSession]);
  const captureSessionEnd = useCallback(() => captureSessionRead().endSession, [captureSessionRead]);

  const value = useMemo<AuthContextValue>(
    () => ({
      ...state, login: startLogin, logout, retry, revalidate, captureSessionRead, captureSessionEnd,
      captureTimingCheck, expireSession, restoreSession,
    }),
    [state, logout, retry, revalidate, captureSessionRead, captureSessionEnd, captureTimingCheck, expireSession, restoreSession],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
