import { StatusMessage } from '../ui/StatusMessage';
import { Navigate, useLocation, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import { startLoginAt } from '../auth/authApi';
import { LoadingScreen } from '../layout/LoadingScreen';

export function LandingPage() {
  const { loading, authenticated, error, logoutAvailable, login, logout, retry, sessionEnded, sessionTraceId } = useAuth();
  const { state } = useLocation();
  const [query] = useSearchParams();
  const loginFailed = query.get('loginError') === '1';
  const savedPath = (state as { returnUrl?: unknown } | null)?.returnUrl;
  const returnUrl = loginFailed ? query.get('returnUrl') : typeof savedPath === 'string' ? savedPath : null;

  if (loading) {
    return <LoadingScreen />;
  }

  if (authenticated) {
    return <Navigate to="/app" replace />;
  }

  return (
    <main className="landing">
      <section className="landing-card">
        <h1>Intelligence Trade</h1>
        <p className="muted">Сопровождение открытых торговых позиций.</p>
        {loginFailed && <StatusMessage kind="error" text="Не удалось завершить вход. Начните вход заново." />}
        {sessionEnded && <StatusMessage kind="error" text="Сессия завершена. Войдите снова." traceId={sessionTraceId} />}
        {error ? (
          <>
            <StatusMessage kind="error" text={error} />
            <div className="landing-actions">
              <button type="button" className="button" onClick={retry}>
                Повторить
              </button>
              {logoutAvailable && (
                <button type="button" className="button button-secondary" onClick={() => void logout()}>
                  Выйти
                </button>
              )}
            </div>
          </>
        ) : (
          <button type="button" className="button" autoFocus={loginFailed} onClick={() => returnUrl ? startLoginAt(returnUrl) : login()}>
            {loginFailed ? 'Начать вход заново' : sessionEnded ? 'Войти снова' : 'Войти'}
          </button>
        )}
      </section>
    </main>
  );
}
