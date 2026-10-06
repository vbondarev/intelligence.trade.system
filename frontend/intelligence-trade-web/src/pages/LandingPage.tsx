import { Navigate } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import { LoadingScreen } from '../layout/LoadingScreen';

export function LandingPage() {
  const { loading, authenticated, error, logoutAvailable, login, logout, retry } = useAuth();

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
        {error ? (
          <>
            <p className="alert" role="alert">
              {error}
            </p>
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
          <button type="button" className="button" onClick={login}>
            Войти
          </button>
        )}
      </section>
    </main>
  );
}
