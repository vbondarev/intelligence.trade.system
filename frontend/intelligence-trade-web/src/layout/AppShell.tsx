import { useCallback, useEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { POSITION_SEARCH_PATH } from '../position/positionNavigation';
import { useAuth } from '../auth/authContext';
import { SessionDeadline } from '../auth/SessionDeadline';
import { RealtimeProvider } from '../realtime/RealtimeProvider';
import { useRealtime } from '../realtime/realtimeContext';
import { createAutoUpdates } from '../realtime/autoUpdates';
import { AutoUpdatesContext, useAutoUpdates, useAutoUpdateState } from '../realtime/autoUpdatesContext';

export function AppShell() {
  const { user } = useAuth();
  return <RealtimeProvider key={user?.userId}><DataUpdatesShell /></RealtimeProvider>;
}

function DataUpdatesShell() {
  const { user, revalidate } = useAuth();
  const [updates] = useState(createAutoUpdates);
  const userId = user?.userId;

  useEffect(() => {
    if (!userId) return;
    const lifetime = new AbortController();
    let checking = false;
    updates.setAccessCheck(() => {
      if (checking) return;
      checking = true;
      void revalidate(userId, lifetime.signal).finally(() => { checking = false; });
    });
    return () => {
      updates.setAccessCheck();
      lifetime.abort();
    };
  }, [userId, revalidate, updates]);

  return <AutoUpdatesContext.Provider value={updates}><AuthenticatedShell /></AutoUpdatesContext.Provider>;
}

function AuthenticatedShell() {
  const { user, error, login, logout } = useAuth();
  const realtime = useRealtime();
  const updates = useAutoUpdates();
  const { mode, announcement } = useAutoUpdateState();
  const [sessionEnded, setSessionEnded] = useState(false);
  const hideUserData = useCallback(() => setSessionEnded(true), []);
  const { pathname } = useLocation();
  const previousPath = useRef(pathname);
  const mainRef = useRef<HTMLElement>(null);

  useEffect(() => {
    const main = mainRef.current;
    const updateTitle = () => {
      const heading = main?.querySelector('h1');
      document.title = `${heading?.textContent ?? 'Обзор'} — Intelligence Trade`;
    };
    const heading = main?.querySelector('h1');
    updateTitle();
    if (previousPath.current !== pathname) {
      heading?.setAttribute('tabindex', '-1');
      heading?.focus();
      previousPath.current = pathname;
    }
    const observer = new MutationObserver(updateTitle);
    if (main) observer.observe(main, { subtree: true, childList: true, characterData: true });
    return () => observer.disconnect();
  }, [pathname]);

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main-content" tabIndex={0}>Перейти к основному содержимому</a>
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
        <NavLink to={POSITION_SEARCH_PATH} className="side-nav-link">
          Поиск позиций
        </NavLink>
      </nav>
      <main ref={mainRef} id="main-content" className="app-main" tabIndex={-1}>
        {user && <SessionDeadline onLogin={login} onExpired={hideUserData} />}
        {!sessionEnded && (
          <div className="auto-updates">
            <button type="button" className="button button-secondary" aria-disabled={mode === 'resuming' || undefined} onClick={updates.toggle}>
              {mode === 'live' ? 'Приостановить автообновление' : 'Возобновить автообновление'}
            </button>
            {mode !== 'live' && <p className="muted">Автообновление приостановлено. Данные могут устареть.</p>}
            {mode === 'resuming' && <p>Возобновляем автообновление…</p>}
            <p aria-live="polite" aria-atomic="true" className={mode === 'failed' ? 'alert' : 'screen-reader-only'}>{announcement}</p>
          </div>
        )}
        {!sessionEnded && mode === 'live' && realtime?.state !== 'ready' && (
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
        {!sessionEnded && <Outlet />}
      </main>
    </div>
  );
}
