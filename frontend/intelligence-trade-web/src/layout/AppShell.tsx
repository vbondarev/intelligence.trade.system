import { NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../auth/authContext';

export function AppShell() {
  const { user, error, logout } = useAuth();

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
      </nav>
      <main className="app-main">
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
