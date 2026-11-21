import { presentError } from '../errors/errorPresentation';
import { StatusMessage } from '../ui/StatusMessage';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import { ObservationDetails } from '../freshness/FreshnessDetails';
import { explicitPresentation, resourceUnavailable, type ScreenReader } from '../realtime/autoUpdates';
import { useAutoUpdates } from '../realtime/autoUpdatesContext';
import { useRealtimeInvalidation } from '../realtime/useRealtimeInvalidation';
import { listConnections, syncConnection } from '../connections/connectionsApi';
import type { ExchangeAccount } from '../connections/connectionTypes';
import { AccountSelector } from '../portfolio/AccountSelector';
import { getPortfolio, listPositions } from '../portfolio/portfolioApi';
import {
  CONNECTION_STATUS_LABELS,
  formatRelativeTime,
  formatTimestamp,
} from '../portfolio/portfolioFormatting';
import type {
  CurrentTrackingState,
  Portfolio,
  PositionFilters,
  PositionListItem,
  PositionListQuery,
  PositionSide,
  PositionsView,
} from '../portfolio/portfolioTypes';
import { PortfolioSummary } from '../portfolio/PortfolioSummary';
import { PositionsSection } from '../portfolio/PositionsSection';

const CONNECTIONS_PATH = '/app/settings/connections';

const POSITIONS_ANNOUNCEMENT_INTERVAL_MS = 10_000;

const ERROR_MESSAGES = {
  exchange_account_disabled: 'Подключение отключено. Восстановите его на странице подключений.',
  exchange_unavailable: 'Bybit сейчас недоступен. Повторите попытку позже.',
  resource_not_found: 'Подключение не найдено.',
  access_forbidden: 'Недостаточно прав для просмотра портфеля.',
} as const;


interface Notice {
  tone: 'success' | 'error';
  text: string;
  traceId?: string | null;
  sessionEnded?: boolean;
  background?: boolean;
}

interface AccountsLoad {
  items: ExchangeAccount[];
  loadedAt: number;
}

/** Состояние ручной синхронизации конкретного подключения; notice относится только к нему. */
interface SyncState {
  running: boolean;
  notice: Notice | null;
}

interface PortfolioLoad {
  accountId: string;
  portfolio: Portfolio | null;
  hasSnapshot: boolean;
  error: Notice | null;
}

interface PositionsLoad {
  key: string;
  /** `reloadToken`, для которого получена первая страница: cursor относится только к этому перечитыванию. */
  reload: number;
  items: PositionListItem[];
  nextCursor: string | null;
  hasMore: boolean;
  loadingMore: boolean;
  error: Notice | null;
}

// UI ветвится только по HTTP status и machine-readable code: title/detail ProblemDetails не показываются.
function toErrorNotice(error: unknown, fallback: string, background = false): Notice {
  const errorNotice = presentError(error, { fallback: fallback, messages: { ...ERROR_MESSAGES, forbidden: ERROR_MESSAGES.access_forbidden, missing: ERROR_MESSAGES.resource_not_found } });
  return { tone: 'error', text: errorNotice.text, traceId: errorNotice.traceId, sessionEnded: errorNotice.action === 'login', background: background && errorNotice.action !== 'login' };
}

/**
 * Выбор подключения: единственное подключение выбирается всегда, подключение из URL — только
 * если оно есть в списке пользователя, иначе первое `connected`. Без `connected` автоматический
 * выбор не выполняется.
 */
function chooseAccount(accounts: readonly ExchangeAccount[], urlAccountId: string | null): ExchangeAccount | null {
  if (accounts.length === 1) {
    return accounts[0] ?? null;
  }

  return (
    accounts.find((account) => account.id === urlAccountId) ??
    accounts.find((account) => account.connectionStatus === 'connected') ??
    null
  );
}

function readFilters(params: URLSearchParams): PositionFilters {
  const view: PositionsView = params.get('view') === 'closed' ? 'closed' : 'current';
  const state = params.get('state');
  const side = params.get('side');
  const symbol = params.get('symbol')?.trim() ?? '';
  return {
    view,
    state: view === 'current' && (state === 'active' || state === 'unknown' || state === 'stale') ? state : null,
    side: side === 'long' || side === 'short' ? side : null,
    symbol: symbol.length > 0 ? symbol : null,
  };
}

function toQuery(accountId: string, filters: PositionFilters): PositionListQuery {
  return {
    exchangeAccountId: accountId,
    trackingState: filters.view === 'closed' ? 'closed' : (filters.state ?? undefined),
    side: filters.side ?? undefined,
    symbol: filters.symbol ?? undefined,
  };
}

export function HomePage() {
  const { login, captureSessionEnd } = useAuth();
  const updates = useAutoUpdates();
  const [searchParams, setSearchParams] = useSearchParams();
  const [accounts, setAccounts] = useState<AccountsLoad | null>(null);
  const [accountsError, setAccountsError] = useState<Notice | null>(null);
  const [portfolioLoad, setPortfolioLoad] = useState<PortfolioLoad | null>(null);
  const [positions, setPositions] = useState<PositionsLoad | null>(null);
  const [syncStates, setSyncStates] = useState<Readonly<Record<string, SyncState>>>({});
  const [reloadToken, setReloadToken] = useState(0);
  const [positionsAnnouncement, setPositionsAnnouncement] = useState('');

  const accountsRead = useRef<ScreenReader | null>(null);
  const portfolioRead = useRef<ScreenReader | null>(null);
  const positionsRead = useRef<ScreenReader | null>(null);
  const accountsGeneration = useRef(0);
  const portfolioGeneration = useRef(0);
  const positionsGeneration = useRef(0);
  const positionsInvalidationVersion = useRef(0);
  const announcedPositionsVersion = useRef(0);
  const lastPositionsAnnouncementAt = useRef<number | null>(null);
  const positionsAnnouncementTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pagination = useRef<AbortController | null>(null);
  const syncInFlight = useRef(new Set<string>());
  const selectedIdRef = useRef<string | null>(null);

  const urlAccountId = searchParams.get('account');
  const filters = readFilters(searchParams);
  const selectedAccount = accounts === null ? null : chooseAccount(accounts.items, urlAccountId);
  const selectedId = selectedAccount?.id ?? null;
  const queryKey = selectedId === null ? null : JSON.stringify(toQuery(selectedId, filters));
  const queryKeyRef = useRef(queryKey);
  useEffect(() => { queryKeyRef.current = queryKey; }, [queryKey]);
  const selectedSync = selectedId === null ? undefined : syncStates[selectedId];

  useEffect(() => {
    selectedIdRef.current = selectedId;
  }, [selectedId]);

  // Каждое чтение делает предыдущие устаревшими: поздний ответ не перезаписывает более новый state.
  const loadAccounts = useCallback((signal?: AbortSignal, view = explicitPresentation): Promise<void> => {
    const endSession = captureSessionEnd();
    const generation = view.kind === 'silent' ? accountsGeneration.current : ++accountsGeneration.current;
    const isCurrent = () => generation === accountsGeneration.current && signal?.aborted !== true;
    return listConnections(signal).then(
      (items) => view.apply(() => {
        if (isCurrent()) {
          setAccounts({ items, loadedAt: Date.now() });
          setAccountsError(null);
        }
      }, isCurrent),
      (error: unknown) => {
        if (isCurrent()) {
          const notice = toErrorNotice(error, 'Не удалось загрузить подключения. Повторите попытку позже.', view.kind === 'live');
          if (view === explicitPresentation) {
            if (notice.sessionEnded) endSession(notice.traceId);
            else updates.reportAccessFailure(error);
          }
          view.fail(error, () => {
            if (isCurrent()) setAccountsError(notice);
          }, () => {
            accountsGeneration.current += 1;
            portfolioGeneration.current += 1;
            positionsGeneration.current += 1;
            pagination.current?.abort();
            setAccounts(null);
            setPortfolioLoad(null);
            setPositions(null);
            if (view.kind !== 'silent') setAccountsError(toErrorNotice(error, 'Не удалось загрузить подключения. Повторите попытку позже.'));
          }, isCurrent);
        }
      },
    );
  }, [captureSessionEnd, updates]);

  useEffect(() => {
    const scheduler = updates.createReader(loadAccounts);
    accountsRead.current = scheduler;
    scheduler.refresh();
    return () => { accountsRead.current = null; scheduler.dispose(); };
  }, [loadAccounts, updates]);

  const updateParams = useCallback(
    (mutate: (params: URLSearchParams) => void, replace = false) => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current);
          mutate(next);
          return next;
        },
        { replace },
      );
    },
    [setSearchParams],
  );

  useEffect(() => {
    if (selectedId !== null && selectedId !== urlAccountId) {
      updateParams((params) => params.set('account', selectedId), true);
    }
  }, [selectedId, urlAccountId, updateParams]);

  useEffect(() => {
    if (selectedId === null) {
      return;
    }

    const scheduler = updates.createReader((signal, view) => {
      const generation = view.kind === 'silent' ? portfolioGeneration.current : ++portfolioGeneration.current;
      const isCurrent = () => generation === portfolioGeneration.current && !signal.aborted;
      return getPortfolio(selectedId, signal).then(
      (portfolio) => view.apply(() => {
        if (isCurrent()) {
          setPortfolioLoad({ accountId: selectedId, portfolio, hasSnapshot: true, error: null });
        }
      }, isCurrent),
      (error: unknown) => {
        if (isCurrent()) {
          view.fail(error, () => {
            if (!isCurrent()) return;
            const notice = toErrorNotice(error, 'Не удалось загрузить портфель. Повторите попытку позже.', view.kind === 'live');
            setPortfolioLoad((current) => {
              const previous = current?.accountId === selectedId ? current : null;
              const hasSnapshot = previous?.hasSnapshot ?? false;
              return {
                accountId: selectedId,
                portfolio: previous?.portfolio ?? null,
                hasSnapshot,
                error: { ...notice, background: notice.background && hasSnapshot },
              };
            });
          }, () => {
            portfolioGeneration.current += 1;
            setPortfolioLoad({ accountId: selectedId, portfolio: null, hasSnapshot: false, error: view.kind === 'silent' ? null : toErrorNotice(error, 'Не удалось загрузить портфель. Повторите попытку позже.') });
          }, isCurrent);
        }
      },
      );
    });
    portfolioRead.current = scheduler;
    scheduler.refresh();
    return () => { portfolioRead.current = null; scheduler.dispose(); };

  }, [selectedId, reloadToken, updates]);

  useEffect(() => {
    if (queryKey === null) {
      return;
    }

    announcedPositionsVersion.current = positionsInvalidationVersion.current;
    const announcePositions = () => {
      positionsAnnouncementTimer.current = null;
      if (!updates.isLive()) return;
      lastPositionsAnnouncementAt.current = Date.now();
      setPositionsAnnouncement((previous) => previous === 'Список позиций обновлён.'
        ? 'Обновлён список позиций.' : 'Список позиций обновлён.');
    };
    const schedulePositionsAnnouncement = () => {
      const previous = lastPositionsAnnouncementAt.current;
      const remaining = previous === null ? 0 : POSITIONS_ANNOUNCEMENT_INTERVAL_MS - (Date.now() - previous);
      if (remaining <= 0) {
        if (positionsAnnouncementTimer.current !== null) clearTimeout(positionsAnnouncementTimer.current);
        announcePositions();
      } else if (positionsAnnouncementTimer.current === null) {
        positionsAnnouncementTimer.current = setTimeout(announcePositions, remaining);
      }
    };
    const query = JSON.parse(queryKey) as PositionListQuery;
    const scheduler = updates.createReader((signal, view) => {
      const generation = view.kind === 'silent' ? positionsGeneration.current : ++positionsGeneration.current;
      const invalidationVersion = positionsInvalidationVersion.current;
      const isCurrent = () => generation === positionsGeneration.current && !signal.aborted && queryKeyRef.current === queryKey;
      return listPositions(query, signal).then(
      (page) => view.apply(() => {
        if (isCurrent()) {
          pagination.current?.abort();
          setPositions({
            key: queryKey,
            reload: reloadToken,
            items: page.items,
            nextCursor: invalidationVersion === positionsInvalidationVersion.current ? page.nextCursor : null,
            hasMore: invalidationVersion === positionsInvalidationVersion.current && page.hasMore,
            loadingMore: false,
            error: null,
          });
          if (view.kind === 'live' && invalidationVersion === positionsInvalidationVersion.current && invalidationVersion > announcedPositionsVersion.current) {
            announcedPositionsVersion.current = invalidationVersion;
            schedulePositionsAnnouncement();
          }
        }
      }, isCurrent),
      (error: unknown) => {
        if (isCurrent()) {
          view.fail(error, () => {
            if (!isCurrent()) return;
            const notice = toErrorNotice(error, 'Не удалось загрузить позиции. Повторите попытку позже.', view.kind === 'live');
            setPositions((current) =>
              current?.key === queryKey
                ? { ...current, loadingMore: false, error: notice }
                : {
                    key: queryKey,
                    reload: reloadToken,
                    items: [],
                    nextCursor: null,
                    hasMore: false,
                    loadingMore: false,
                    error: notice,
                  },
            );
          }, () => {
            positionsGeneration.current += 1;
            pagination.current?.abort();
            setPositions({ key: queryKey, reload: reloadToken, items: [], nextCursor: null, hasMore: false, loadingMore: false,
              error: view.kind === 'silent' ? null : toErrorNotice(error, 'Не удалось загрузить позиции. Повторите попытку позже.') });
          }, isCurrent);
        }
      },
      );
    });
    positionsRead.current = scheduler;
    scheduler.refresh();
    return () => {
      positionsRead.current = null;
      pagination.current?.abort();
      scheduler.dispose();
      if (positionsAnnouncementTimer.current !== null) clearTimeout(positionsAnnouncementTimer.current);
      positionsAnnouncementTimer.current = null;
    };

  }, [queryKey, reloadToken, updates]);

  useEffect(() => updates.subscribe(() => {
    if (updates.isLive()) return;
    if (positionsAnnouncementTimer.current !== null) clearTimeout(positionsAnnouncementTimer.current);
    positionsAnnouncementTimer.current = null;
    announcedPositionsVersion.current = positionsInvalidationVersion.current;
    setPositionsAnnouncement('');
  }), [updates]);

  useRealtimeInvalidation((event) => {
    if (event.type === 'recovery' || event.type === 'exchangeAccount.updated') {
      if (event.type === 'recovery') {
        if (updates.isLive()) accountsGeneration.current += 1;
        accountsRead.current?.recover();
      }
      else accountsRead.current?.invalidate();
    }
    if (event.type === 'recovery' || (event.type === 'portfolio.updated' && event.exchangeAccountId === selectedId)) {
      if (event.type === 'recovery') {
        if (updates.isLive()) portfolioGeneration.current += 1;
        portfolioRead.current?.recover();
      }
      else portfolioRead.current?.invalidate();
    }
    if (event.type === 'recovery' || event.type === 'position.updated') {
      if (updates.isLive()) {
        if (positionsAnnouncementTimer.current !== null) clearTimeout(positionsAnnouncementTimer.current);
        positionsAnnouncementTimer.current = null;
        pagination.current?.abort();
        positionsInvalidationVersion.current += 1;
        setPositions((current) => current?.key === queryKey
          ? { ...current, nextCursor: null, hasMore: false, loadingMore: false } : current);
      }
      if (event.type === 'recovery') {
        if (updates.isLive()) positionsGeneration.current += 1;
        positionsRead.current?.recover();
      }
      else positionsRead.current?.invalidate();
    }
  });

  // Следующая страница относится к текущему поколению первой страницы: смена подключения,
  // фильтра или reload после sync отменяет её и не даёт дописать устаревшие позиции. Пока после
  // sync не получена новая первая страница, прежний cursor не используется.
  const handleLoadMore = () => {
    if (
      positions === null ||
      positions.key !== queryKey ||
      positions.reload !== reloadToken ||
      positions.nextCursor === null ||
      positions.loadingMore
    ) {
      return;
    }

    updates.cancelResume();
    const key = positions.key;
    const endSession = captureSessionEnd();
    const query: PositionListQuery = { ...(JSON.parse(key) as PositionListQuery), cursor: positions.nextCursor };
    const generation = positionsGeneration.current;
    const invalidationVersion = positionsInvalidationVersion.current;
    const controller = new AbortController();
    pagination.current?.abort();
    pagination.current = controller;
    const signal = controller.signal;
    const isCurrent = () => generation === positionsGeneration.current
      && invalidationVersion === positionsInvalidationVersion.current && !signal.aborted
      && queryKeyRef.current === key;
    setPositions((current) => (current?.key === key ? { ...current, loadingMore: true, error: null } : current));
    listPositions(query, signal).then(
      (page) => {
        if (isCurrent()) {
          setPositions((current) =>
            current?.key === key
              ? {
                  ...current,
                  items: [...current.items, ...page.items],
                  nextCursor: page.nextCursor,
                  hasMore: page.hasMore,
                  loadingMore: false,
                }
              : current,
          );
        }
      },
      (error: unknown) => {
        if (isCurrent()) {
          const notice = toErrorNotice(error, 'Не удалось загрузить позиции. Повторите попытку позже.');
          if (notice.sessionEnded) endSession(notice.traceId);
          else updates.reportAccessFailure(error);
          const revoked = resourceUnavailable(error);
          setPositions((current) =>
            current?.key === key
              ? {
                  ...current,
                  ...(revoked ? { items: [], nextCursor: null, hasMore: false } : {}),
                  loadingMore: false,
                  error: notice,
                }
              : current,
          );
        }
      },
    );
  };

  // Повтор синхронизации выполняет только пользователь. После завершения factual state
  // перечитывается и при ошибке: backend мог обновить статус, freshness или tracking state.
  // Синхронизация привязана к подключению, выбранному в момент запуска: если пользователь
  // уже перешёл на другое, портфель и позиции нового выбора не перечитываются и не подменяются.
  const handleSync = async (account: ExchangeAccount) => {
    const accountId = account.id;
    const endSession = captureSessionEnd();
    if (account.connectionStatus === 'disabled' || syncInFlight.current.has(accountId)) {
      return;
    }
    updates.cancelResume();

    const updateSync = (state: SyncState) => setSyncStates((current) => ({ ...current, [accountId]: state }));
    syncInFlight.current.add(accountId);
    updateSync({ running: true, notice: null });
    let notice: Notice;
    try {
      await syncConnection(accountId);
      notice = { tone: 'success', text: 'Синхронизация завершена.' };
    } catch (error) {
      notice = toErrorNotice(error, 'Не удалось синхронизировать подключение. Повторите попытку позже.');
      if (notice.sessionEnded) endSession(notice.traceId);
    }
    updateSync({ running: true, notice });

    if (notice.sessionEnded !== true) {
      await loadAccounts();
      if (selectedIdRef.current === accountId) {
        setReloadToken((token) => token + 1);
      }
    }

    syncInFlight.current.delete(accountId);
    updateSync({ running: false, notice });
  };

  // Завершённые результаты синхронизации других подключений скрываются при смене выбора;
  // выполняющаяся синхронизация сохраняет своё состояние и покажет результат при возврате.
  const handleSelectAccount = (id: string) => {
    setSyncStates((current) =>
      Object.fromEntries(Object.entries(current).filter(([accountId, state]) => state.running || accountId === id)),
    );
    updateParams((params) => params.set('account', id));
  };

  const handleViewChange = (view: PositionsView) =>
    updateParams((params) => {
      if (view === 'closed') {
        params.set('view', 'closed');
        params.delete('state');
      } else {
        params.delete('view');
      }
    });

  const handleStateChange = (state: CurrentTrackingState | null) =>
    updateParams((params) => (state === null ? params.delete('state') : params.set('state', state)));

  const handleSideChange = (side: PositionSide | null) =>
    updateParams((params) => (side === null ? params.delete('side') : params.set('side', side)));

  const handleSymbolChange = (symbol: string | null) =>
    updateParams((params) => (symbol === null ? params.delete('symbol') : params.set('symbol', symbol)));

  const currentPortfolio = portfolioLoad !== null && portfolioLoad.accountId === selectedId ? portfolioLoad : null;
  const currentPositions = positions !== null && positions.key === queryKey ? positions : null;

  return (
    <section className="page home-page">
      <h1>Обзор</h1>
      <p className="screen-reader-only" role="status" aria-atomic="true">{positionsAnnouncement}</p>

      {accountsError && (
        <NoticeMessage
          notice={{ ...accountsError, background: accountsError.background && accounts !== null }}
          onLogin={login}
          onRetry={() => accountsRead.current?.refresh()}
        />
      )}
      {accounts === null && accountsError === null && (
        <StatusMessage kind="loading" text="Загружаем подключения…" />
      )}

      {accounts !== null && accounts.items.length === 0 && (
        <div className="panel empty-state">
          <p className="muted">Подключений пока нет. Подключите read-only ключ Bybit, чтобы увидеть портфель.</p>
          <Link to={CONNECTIONS_PATH} className="button">
            Подключить биржу
          </Link>
        </div>
      )}

      {accounts !== null && accounts.items.length > 1 && (
        <AccountSelector accounts={accounts.items} selectedId={selectedId} onSelect={handleSelectAccount} />
      )}

      {accounts !== null && accounts.items.length > 1 && selectedAccount === null && (
        <p className="muted">Нет активных подключений. Выберите подключение, чтобы посмотреть сохранённые данные.</p>
      )}

      {accounts !== null && selectedAccount !== null && (
        <>
          <SelectedAccountPanel
            account={selectedAccount}
            portfolio={currentPortfolio?.portfolio ?? null}
            now={accounts.loadedAt}
            syncing={selectedSync?.running === true}
            onSync={() => void handleSync(selectedAccount)}
          />
          {selectedSync?.notice && <NoticeMessage notice={selectedSync.notice} onLogin={login} />}

          {currentPortfolio === null && (
            <StatusMessage kind="loading" text="Загружаем портфель…" />
          )}
          {currentPortfolio?.error && <NoticeMessage notice={currentPortfolio.error} onLogin={login} onRetry={() => portfolioRead.current?.refresh()} />}
          {currentPortfolio?.hasSnapshot && currentPortfolio.portfolio === null && (
            <StatusMessage kind="empty" text="Данных портфеля пока нет. Запустите синхронизацию подключения." />
          )}
          {currentPortfolio?.portfolio && <PortfolioSummary portfolio={currentPortfolio.portfolio} />}

          <PositionsSection
            filters={filters}
            accountName={selectedAccount.displayName}
            list={{
              loading: currentPositions === null,
              items: currentPositions?.items ?? [],
              hasMore: currentPositions !== null && currentPositions.reload === reloadToken && currentPositions.hasMore,
              loadingMore: currentPositions?.loadingMore ?? false,
              error: currentPositions?.error ? <NoticeMessage notice={currentPositions.error} onLogin={login} onRetry={() => positionsRead.current?.refresh()} /> : null,
            }}
            onViewChange={handleViewChange}
            onStateChange={handleStateChange}
            onSideChange={handleSideChange}
            onSymbolChange={handleSymbolChange}
            onLoadMore={handleLoadMore}
          />
        </>
      )}
    </section>
  );
}

function SelectedAccountPanel({
  account,
  portfolio,
  now,
  syncing,
  onSync,
}: {
  account: ExchangeAccount;
  portfolio: Portfolio | null;
  now: number;
  syncing: boolean;
  onSync: () => void;
}) {
  const titleId = useId();
  const disabled = account.connectionStatus === 'disabled';

  return (
    <section className="panel account-panel" aria-labelledby={titleId}>
      <header className="connection-header">
        <h2 id={titleId} className="connection-title">
          {account.displayName}
        </h2>
        <span className={`connection-status connection-status-${account.connectionStatus}`}>
          {CONNECTION_STATUS_LABELS[account.connectionStatus]}
        </span>
      </header>
      <dl className="connection-details">
        <ObservationDetails observation={account.realtimeObservation} />
        <div>
          <dt>Обновлено</dt>
          <dd>
            {account.lastSyncedAt === null ? (
              'Синхронизаций ещё не было'
            ) : (
              <time dateTime={account.lastSyncedAt} title={formatTimestamp(account.lastSyncedAt)}>
                {formatRelativeTime(account.lastSyncedAt, now)}
              </time>
            )}
          </dd>
        </div>
      </dl>

      {portfolio !== null && (
        <ul className="data-state">
          {portfolio.isFresh ? (
            <li className="data-state-ok">REST-снимок актуален на момент ответа</li>
          ) : (
            <li className="data-state-warning">
              REST-снимок устарел
              {account.lastSyncedAt !== null && (
                <>
                  . Последняя успешная синхронизация:{' '}
                  <time dateTime={account.lastSyncedAt}>{formatTimestamp(account.lastSyncedAt)}</time>
                </>
              )}
            </li>
          )}
          {!portfolio.isComplete && <li className="data-state-warning">Данные неполные</li>}
        </ul>
      )}
      {account.connectionStatus === 'unavailable' && (
        <p className="muted">Подключение недоступно: показаны последние сохранённые данные.</p>
      )}
      {disabled && <p className="muted">Подключение отключено: синхронизация недоступна.</p>}

      <div className="connection-actions">
        <button type="button" className="button" disabled={disabled || syncing} onClick={onSync}>
          {syncing ? 'Синхронизация…' : 'Синхронизировать'}
        </button>
      </div>
    </section>
  );
}

function NoticeMessage({ notice, onLogin, onRetry }: { notice: Notice; onLogin: () => void; onRetry?: () => void }) {
  const actions = notice.sessionEnded && (
    <button type="button" className="button button-secondary" onClick={onLogin}>
      Войти снова
    </button>
  ) || (onRetry && <button type="button" className="button button-secondary" onClick={onRetry}>Повторить</button>);
  return <StatusMessage kind={notice.background ? 'warning' : notice.tone} text={notice.text} traceId={notice.traceId} actions={actions} />;
}
