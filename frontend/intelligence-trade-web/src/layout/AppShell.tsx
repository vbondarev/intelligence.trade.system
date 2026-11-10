import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import { RealtimeProvider } from '../realtime/RealtimeProvider';
import { useRealtime } from '../realtime/realtimeContext';

export function AppShell() {
  const { user } = useAuth();
  return <RealtimeProvider key={user?.userId}><AuthenticatedShell /></RealtimeProvider>;
}

function AuthenticatedShell() {
  const { user, error, logout } = useAuth();
  const realtime = useRealtime();

  return (
    <div className="app-shell">
      <header className="topbar">
        <span className="brand">Intelligence Trade</span>
        <div className="topbar-actions">
          {user && <span className="topbar-user">{user.subject}</span>}
          <button type="button" className="button button-secondary" onClick={() => void logout()}>
            Выйти
          </button>
        </div>
      </header>
      <nav className="side-nav" aria-label="Основная навигация">
        <NavLink to="/app" end className="side-nav-link">
          Обзор
        </NavLink>
        <NavLink to="/app/settings/connections" className="side-nav-link">
          Подключения
        </NavLink>
      </nav>
      <main className="app-main">
        {realtime?.state !== 'ready' && (
          <div aria-live="polite">
            <p>Realtime: {realtime?.state === 'offline' ? 'соединение недоступно' : 'подключение…'}</p>
            {realtime?.state === 'offline' && <button type="button" className="button button-secondary" onClick={realtime.retry}>Повторить подключение</button>}
          </div>
        )}
        {error && (
          <p className="alert" role="alert">
            {error}
          </p>
        )}
        <Outlet />
      </main>
    </div>
  );
}
