import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { AuthApiError, fetchSession, logout as logoutRequest, startLogin } from './authApi';
import { AuthContext } from './authContext';
import type { AuthContextValue, AuthState, BrowserSession } from './authTypes';

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
  const [state, setState] = useState<AuthState>(LOADING_STATE);
  const [attempt, setAttempt] = useState(0);
  const epoch = useRef(0);
  const currentUserId = useRef<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    const requestedEpoch = ++epoch.current;
    fetchSession(controller.signal).then(
      (session) => {
        if (!controller.signal.aborted && epoch.current === requestedEpoch) {
          epoch.current += 1;
          currentUserId.current = session.authenticated ? session.user.userId : null;
          setState(toAuthState(session));
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted && epoch.current === requestedEpoch) {
          epoch.current += 1;
          const status = error instanceof AuthApiError ? error.status : 0;
          setState({
            loading: false,
            authenticated: false,
            user: null,
            error: SESSION_ERROR,
            // 403 и 503 сохраняют server-side session, поэтому full logout должен оставаться доступен.
            logoutAvailable: status === 403 || status === 503,
          });
        }
      },
    );
    return () => controller.abort();
  }, [attempt]);

  const retry = useCallback(() => {
    epoch.current += 1;
    currentUserId.current = null;
    setState(LOADING_STATE);
    setAttempt((current) => current + 1);
  }, []);

  const logout = useCallback(async () => {
    const requestedEpoch = epoch.current;
    try {
      await logoutRequest();
    } catch {
      if (epoch.current === requestedEpoch) setState((current) => ({ ...current, error: LOGOUT_ERROR }));
    }
  }, []);

  const revalidate = useCallback(async (expectedUserId: string, signal: AbortSignal) => {
    const canReconnect = () => !signal.aborted && currentUserId.current === expectedUserId;
    if (!canReconnect()) return false;

    const requestedEpoch = ++epoch.current;
    try {
      const session = await fetchSession(signal);
      if (!canReconnect() || epoch.current !== requestedEpoch) return canReconnect();
      // Подтверждение сессии отзывает и право чтений, начатых во время проверки.
      epoch.current += 1;
      currentUserId.current = session.authenticated ? session.user.userId : null;
      if (currentUserId.current !== expectedUserId) setState(toAuthState(session));
      return canReconnect();
    } catch (error) {
      if (!canReconnect() || epoch.current !== requestedEpoch) return canReconnect();
      if (error instanceof AuthApiError && error.status === 403) {
        epoch.current += 1;
        currentUserId.current = null;
        setState({ ...toAuthState({ authenticated: false }), error: SESSION_ERROR, logoutAvailable: true });
        return false;
      }
      return true;
    }
  }, []);

  const captureSessionEnd = useCallback(() => {
    const requestedEpoch = epoch.current;
    return (traceId?: string | null) => {
      if (epoch.current !== requestedEpoch) return;
      epoch.current += 1;
      currentUserId.current = null;
      setState({ ...toAuthState({ authenticated: false }), sessionEnded: true, sessionTraceId: traceId });
    };
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ ...state, login: startLogin, logout, retry, revalidate, captureSessionEnd }),
    [state, logout, retry, revalidate, captureSessionEnd],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
