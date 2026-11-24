import { StatusMessage } from '../ui/StatusMessage';
import { useEffect, useLayoutEffect, useRef, useState, type MouseEvent } from 'react';
import { matchPath, NavLink, Outlet, useLocation } from 'react-router-dom';
import { POSITION_SEARCH_PATH } from '../position/positionNavigation';
import { useAuth } from '../auth/authContext';
import { startLoginAfterExpiry } from '../auth/authApi';
import { SessionDeadline } from '../auth/SessionDeadline';
import { RealtimeProvider } from '../realtime/RealtimeProvider';
import { useRealtime } from '../realtime/realtimeContext';
import { createAutoUpdates } from '../realtime/autoUpdates';
import { AutoUpdatesContext, useAutoUpdates, useAutoUpdateState } from '../realtime/autoUpdatesContext';

interface PositionLinkPress {
  link: HTMLAnchorElement;
  from: string;
  to: string;
  focusedMain: boolean;
}

function getPositionLink(event: MouseEvent<HTMLDivElement>, main: HTMLElement | null) {
  if (!event.isTrusted || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return null;
  const target = event.target;
  if (!(target instanceof Element)) return null;
  const link = target.closest('a[href]');
  if (!(link instanceof HTMLAnchorElement) || !main?.contains(link)) return null;
  if (link.origin !== window.location.origin || link.hasAttribute('download')) return null;
  if (link.target && link.target.toLowerCase() !== '_self') return null;
  if (!matchPath('/app/positions/:id', link.pathname) || matchPath(POSITION_SEARCH_PATH, link.pathname)) return null;
  return link;
}

export function AppShell() {
  const { user, logout, sessionExpired, sessionInstance } = useAuth();
  const { pathname, search, hash } = useLocation();
  const route = pathname + search + hash;
  const returnRoute = useRef(route);
  const previousPath = useRef(pathname);
  const mainRef = useRef<HTMLElement>(null);
  const positionLinkPress = useRef<PositionLinkPress | null>(null);
  const automaticMainFocusPath = useRef<string | null>(null);

  function cancelPositionLinkFocus() {
    positionLinkPress.current = null;
    automaticMainFocusPath.current = null;
  }

  function capturePositionLinkPress(event: MouseEvent<HTMLDivElement>) {
    cancelPositionLinkFocus();
    const main = mainRef.current;
    const link = getPositionLink(event, main);
    const from = window.location.pathname;
    if (!link || link.pathname === from || document.activeElement === main) return;
    positionLinkPress.current = { link, from, to: link.pathname, focusedMain: false };
  }

  function confirmPositionLinkClick(event: MouseEvent<HTMLDivElement>) {
    const press = positionLinkPress.current;
    cancelPositionLinkFocus();
    if (!press?.focusedMain || event.detail === 0 || document.activeElement !== mainRef.current) return;
    if (getPositionLink(event, mainRef.current) !== press.link) return;
    // Link записывает новый URL до всплытия клика к оболочке.
    const currentPath = window.location.pathname;
    if (currentPath !== press.from && currentPath === press.to) automaticMainFocusPath.current = currentPath;
  }

  useLayoutEffect(() => {
    if (!sessionExpired) returnRoute.current = route;
  }, [route, sessionExpired]);

  useEffect(() => {
    const main = mainRef.current;
    if (!main) return;

    if (sessionExpired || automaticMainFocusPath.current !== pathname) automaticMainFocusPath.current = null;
    positionLinkPress.current = null;
    const trackFocus = (event: FocusEvent) => {
      automaticMainFocusPath.current = null;
      const press = positionLinkPress.current;
      if (!press) return;
      if (event.target === main) press.focusedMain = true;
      else positionLinkPress.current = null;
    };
    const clearOnWindowBlur = () => {
      positionLinkPress.current = null;
      automaticMainFocusPath.current = null;
    };
    document.addEventListener('focusin', trackFocus);
    window.addEventListener('blur', clearOnWindowBlur);
    const focusHeading = (heading: HTMLHeadingElement) => {
      heading.setAttribute('tabindex', '-1');
      heading.focus();
    };
    const updateTitle = () => {
      const heading = main.querySelector('h1');
      document.title = `${heading?.textContent ?? 'Обзор'} — Intelligence Trade`;
    };
    updateTitle();
    let waitingForHeading = false;
    if (previousPath.current !== pathname) {
      previousPath.current = pathname;
      if (!sessionExpired) {
        const heading = main.querySelector<HTMLHeadingElement>('h1:not([data-route-pending])');
        if (heading) {
          automaticMainFocusPath.current = null;
          focusHeading(heading);
        } else {
          waitingForHeading = true;
        }
      }
    }
    const observer = new MutationObserver(() => {
      updateTitle();
      if (!waitingForHeading) return;
      const heading = main.querySelector<HTMLHeadingElement>('h1:not([data-route-pending])');
      if (!heading) return;
      waitingForHeading = false;
      const active = document.activeElement;
      const automaticMainFocus = active === main && automaticMainFocusPath.current === pathname;
      if (automaticMainFocusPath.current === pathname) automaticMainFocusPath.current = null;
      if (active === document.body || automaticMainFocus) focusHeading(heading);
    });
    observer.observe(main, { subtree: true, childList: true, characterData: true });
    return () => {
      observer.disconnect();
      document.removeEventListener('focusin', trackFocus);
      window.removeEventListener('blur', clearOnWindowBlur);
      if (automaticMainFocusPath.current === pathname) automaticMainFocusPath.current = null;
    };
  }, [pathname, sessionExpired]);

  return (
    <div
      className="app-shell"
      onMouseDownCapture={capturePositionLinkPress}
      onClick={confirmPositionLinkClick}
      onKeyDownCapture={() => {
        positionLinkPress.current = null;
      }}
      onPointerCancelCapture={cancelPositionLinkFocus}
      onContextMenuCapture={cancelPositionLinkFocus}
    >
      {!sessionExpired && (
        <>
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
        </>
      )}
      <main ref={mainRef} id="main-content" className="app-main" tabIndex={-1}>
        <SessionDeadline key={sessionInstance} onLogin={() => startLoginAfterExpiry(returnRoute.current)} />
        {!sessionExpired && user && (
          <RealtimeProvider key={user.userId}><DataUpdatesShell /></RealtimeProvider>
        )}
      </main>
    </div>
  );
}

function DataUpdatesShell() {
  const { user, revalidate, captureSessionRead } = useAuth();
  const [updates] = useState(() => createAutoUpdates(captureSessionRead));
  const userId = user?.userId;

  useEffect(() => {
    if (!userId) return;
    const lifetime = new AbortController();
    let checking = false;
    updates.setAccessCheck((status) => {
      if (status !== 403 || checking) return;
      checking = true;
      void revalidate(userId, lifetime.signal).finally(() => { checking = false; });
    });
    return () => {
      updates.cancelResume();
      updates.setAccessCheck();
      lifetime.abort();
    };
  }, [userId, revalidate, updates]);

  return <AutoUpdatesContext.Provider value={updates}><AuthenticatedContent /></AutoUpdatesContext.Provider>;
}

function AuthenticatedContent() {
  const { error } = useAuth();
  const realtime = useRealtime();
  const updates = useAutoUpdates();
  const { mode, announcement } = useAutoUpdateState();

  return (
    <>
      <div className="auto-updates">
        <button type="button" className="button button-secondary" aria-disabled={mode === 'resuming' || undefined} onClick={updates.toggle}>
          {mode === 'live' ? 'Приостановить автообновление' : 'Возобновить автообновление'}
        </button>
        {mode !== 'live' && <p className="muted">Автообновление приостановлено. Данные могут устареть.</p>}
        {mode === 'resuming' && <p>Возобновляем автообновление…</p>}
        <p aria-live="polite" aria-atomic="true" className={mode === 'failed' ? 'alert' : 'screen-reader-only'}>{announcement}</p>
      </div>
      {mode === 'live' && realtime?.state !== 'ready' && (
        <div aria-live="polite">
          <p>Realtime: {realtime?.state === 'offline' ? 'соединение недоступно' : 'подключение…'}</p>
          {realtime?.state === 'offline' && <button type="button" className="button button-secondary" onClick={realtime.retry}>Повторить подключение</button>}
        </div>
      )}
      {error && <StatusMessage kind="error" text={error} />}
      <Outlet />
    </>
  );
}
