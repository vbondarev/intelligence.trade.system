import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
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

  useEffect(() => {
    const controller = new AbortController();
    fetchSession(controller.signal).then(
      (session) => {
        if (!controller.signal.aborted) {
          setState(toAuthState(session));
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
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
    setState(LOADING_STATE);
    setAttempt((current) => current + 1);
  }, []);

  const logout = useCallback(async () => {
    try {
      await logoutRequest();
    } catch {
      setState((current) => ({ ...current, error: LOGOUT_ERROR }));
    }
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ ...state, login: startLogin, logout, retry }),
    [state, logout, retry],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
