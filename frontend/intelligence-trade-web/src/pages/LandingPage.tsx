import { Navigate } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import { LoadingScreen } from '../layout/LoadingScreen';

export function LandingPage() {
  const { loading, authenticated, error, login, retry } = useAuth();

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
            <button type="button" className="button" onClick={retry}>
              Повторить
            </button>
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
