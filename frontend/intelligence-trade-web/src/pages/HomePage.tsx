import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import { ConnectionsApiError, listConnections, syncConnection } from '../connections/connectionsApi';
import type { ExchangeAccount } from '../connections/connectionTypes';
import { AccountSelector } from '../portfolio/AccountSelector';
import { PortfolioApiError, getPortfolio, listPositions } from '../portfolio/portfolioApi';
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

const SESSION_ENDED = 'Сессия завершена. Войдите снова.';
const SERVICE_UNAVAILABLE = 'Сервис временно недоступен. Повторите попытку позже.';

const ERROR_MESSAGES = {
  exchange_account_disabled: 'Подключение отключено. Восстановите его на странице подключений.',
  exchange_unavailable: 'Bybit сейчас недоступен. Повторите попытку позже.',
  resource_not_found: 'Подключение не найдено.',
  access_forbidden: 'Недостаточно прав для просмотра портфеля.',
} as const;

type ErrorCode = keyof typeof ERROR_MESSAGES;

interface Notice {
  tone: 'success' | 'error';
  text: string;
  traceId?: string | null;
  sessionEnded?: boolean;
}

interface AccountsLoad {
  items: ExchangeAccount[];
  loadedAt: number;
}

type PortfolioLoad =
  | { accountId: string; portfolio: Portfolio | null; error: null }
  | { accountId: string; portfolio: null; error: Notice };

interface PositionsLoad {
  key: string;
  items: PositionListItem[];
  nextCursor: string | null;
  hasMore: boolean;
  loadingMore: boolean;
  error: Notice | null;
}

// UI ветвится только по HTTP status и machine-readable code: title/detail ProblemDetails не показываются.
function toErrorNotice(error: unknown, fallback: string): Notice {
  if (!(error instanceof ConnectionsApiError || error instanceof PortfolioApiError)) {
    return { tone: 'error', text: fallback };
  }

  if (error.status === 401 || error.code === 'authentication_required') {
    return { tone: 'error', text: SESSION_ENDED, sessionEnded: true };
  }

  let text: string | undefined =
    error.code !== null && Object.hasOwn(ERROR_MESSAGES, error.code) ? ERROR_MESSAGES[error.code as ErrorCode] : undefined;
  if (text === undefined) {
    if (error.status === 403) {
      text = ERROR_MESSAGES.access_forbidden;
    } else if (error.status === 404) {
      text = ERROR_MESSAGES.resource_not_found;
    } else if (error.status === 503) {
      text = SERVICE_UNAVAILABLE;
    } else {
      text = fallback;
    }
  }

  return { tone: 'error', text, traceId: error.traceId };
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
  const { login } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const [accounts, setAccounts] = useState<AccountsLoad | null>(null);
  const [accountsError, setAccountsError] = useState<Notice | null>(null);
  const [portfolioLoad, setPortfolioLoad] = useState<PortfolioLoad | null>(null);
  const [positions, setPositions] = useState<PositionsLoad | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [syncNotice, setSyncNotice] = useState<Notice | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const accountsGeneration = useRef(0);
  const portfolioGeneration = useRef(0);
  const positionsGeneration = useRef(0);
  const positionsController = useRef<AbortController | null>(null);
  const syncInFlight = useRef(false);

  const urlAccountId = searchParams.get('account');
  const filters = readFilters(searchParams);
  const selectedAccount = accounts === null ? null : chooseAccount(accounts.items, urlAccountId);
  const selectedId = selectedAccount?.id ?? null;
  const queryKey = selectedId === null ? null : JSON.stringify(toQuery(selectedId, filters));

  // Каждое чтение делает предыдущие устаревшими: поздний ответ не перезаписывает более новый state.
  const loadAccounts = useCallback((signal?: AbortSignal): Promise<void> => {
    const generation = ++accountsGeneration.current;
    const isCurrent = () => generation === accountsGeneration.current && signal?.aborted !== true;
    return listConnections(signal).then(
      (items) => {
        if (isCurrent()) {
          setAccounts({ items, loadedAt: Date.now() });
          setAccountsError(null);
        }
      },
      (error: unknown) => {
        if (isCurrent()) {
          setAccountsError(toErrorNotice(error, 'Не удалось загрузить подключения. Повторите попытку позже.'));
        }
      },
    );
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void loadAccounts(controller.signal);
    return () => controller.abort();
  }, [loadAccounts]);

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

    const controller = new AbortController();
    const generation = ++portfolioGeneration.current;
    const isCurrent = () => generation === portfolioGeneration.current && !controller.signal.aborted;
    getPortfolio(selectedId, controller.signal).then(
      (portfolio) => {
        if (isCurrent()) {
          setPortfolioLoad({ accountId: selectedId, portfolio, error: null });
        }
      },
      (error: unknown) => {
        if (isCurrent()) {
          setPortfolioLoad({
            accountId: selectedId,
            portfolio: null,
            error: toErrorNotice(error, 'Не удалось загрузить портфель. Повторите попытку позже.'),
          });
        }
      },
    );
    return () => controller.abort();
  }, [selectedId, reloadToken]);

  useEffect(() => {
    if (queryKey === null) {
      return;
    }

    const query = JSON.parse(queryKey) as PositionListQuery;
    const controller = new AbortController();
    positionsController.current = controller;
    const generation = ++positionsGeneration.current;
    const isCurrent = () => generation === positionsGeneration.current && !controller.signal.aborted;
    listPositions(query, controller.signal).then(
      (page) => {
        if (isCurrent()) {
          setPositions({
            key: queryKey,
            items: page.items,
            nextCursor: page.nextCursor,
            hasMore: page.hasMore,
            loadingMore: false,
            error: null,
          });
        }
      },
      (error: unknown) => {
        if (isCurrent()) {
          setPositions({
            key: queryKey,
            items: [],
            nextCursor: null,
            hasMore: false,
            loadingMore: false,
            error: toErrorNotice(error, 'Не удалось загрузить позиции. Повторите попытку позже.'),
          });
        }
      },
    );
    return () => controller.abort();
  }, [queryKey, reloadToken]);

  // Следующая страница относится к текущему поколению первой страницы: смена подключения,
  // фильтра или reload после sync отменяет её и не даёт дописать устаревшие позиции.
  const handleLoadMore = () => {
    if (positions === null || positions.key !== queryKey || positions.nextCursor === null || positions.loadingMore) {
      return;
    }

    const key = positions.key;
    const query: PositionListQuery = { ...(JSON.parse(key) as PositionListQuery), cursor: positions.nextCursor };
    const generation = positionsGeneration.current;
    const signal = positionsController.current?.signal;
    const isCurrent = () => generation === positionsGeneration.current && signal?.aborted !== true;
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
          setPositions((current) =>
            current?.key === key
              ? {
                  ...current,
                  loadingMore: false,
                  error: toErrorNotice(error, 'Не удалось загрузить позиции. Повторите попытку позже.'),
                }
              : current,
          );
        }
      },
    );
  };

  // Повтор синхронизации выполняет только пользователь. После завершения factual state
  // перечитывается и при ошибке: backend мог обновить статус, freshness или tracking state.
  const handleSync = async () => {
    if (selectedAccount === null || selectedAccount.connectionStatus === 'disabled' || syncInFlight.current) {
      return;
    }

    syncInFlight.current = true;
    setSyncing(true);
    setSyncNotice(null);
    let sessionEnded = false;
    try {
      await syncConnection(selectedAccount.id);
      setSyncNotice({ tone: 'success', text: 'Синхронизация завершена.' });
    } catch (error) {
      const notice = toErrorNotice(error, 'Не удалось синхронизировать подключение. Повторите попытку позже.');
      sessionEnded = notice.sessionEnded === true;
      setSyncNotice(notice);
    }

    if (!sessionEnded) {
      await loadAccounts();
      setReloadToken((token) => token + 1);
    }

    syncInFlight.current = false;
    setSyncing(false);
  };

  const handleSelectAccount = (id: string) => {
    setSyncNotice(null);
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

      {accountsError && <NoticeMessage notice={accountsError} onLogin={login} />}
      {accounts === null && accountsError === null && (
        <p className="inline-status" role="status">
          <span className="spinner" aria-hidden="true" />
          Загружаем подключения…
        </p>
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
            syncing={syncing}
            onSync={() => void handleSync()}
          />
          {syncNotice && <NoticeMessage notice={syncNotice} onLogin={login} />}

          {currentPortfolio === null && (
            <p className="inline-status" role="status">
              <span className="spinner" aria-hidden="true" />
              Загружаем портфель…
            </p>
          )}
          {currentPortfolio?.error && <NoticeMessage notice={currentPortfolio.error} onLogin={login} />}
          {currentPortfolio !== null && currentPortfolio.error === null && currentPortfolio.portfolio === null && (
            <p className="muted">Данных портфеля пока нет. Запустите синхронизацию подключения.</p>
          )}
          {currentPortfolio?.portfolio && <PortfolioSummary portfolio={currentPortfolio.portfolio} />}

          <PositionsSection
            filters={filters}
            list={{
              loading: currentPositions === null,
              items: currentPositions?.items ?? [],
              hasMore: currentPositions?.hasMore ?? false,
              loadingMore: currentPositions?.loadingMore ?? false,
              error: currentPositions?.error ? <NoticeMessage notice={currentPositions.error} onLogin={login} /> : null,
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
            <li className="data-state-ok">Данные актуальны</li>
          ) : (
            <li className="data-state-warning">
              Данные устарели
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

function NoticeMessage({ notice, onLogin }: { notice: Notice; onLogin: () => void }) {
  if (notice.tone === 'success') {
    return (
      <p className="notice notice-success" role="status">
        {notice.text}
      </p>
    );
  }

  return (
    <div className="alert" role="alert">
      <p className="notice-text">{notice.text}</p>
      {notice.traceId && <p className="notice-trace">Код диагностики: {notice.traceId}</p>}
      {notice.sessionEnded && (
        <button type="button" className="button button-secondary" onClick={onLogin}>
          Войти снова
        </button>
      )}
    </div>
  );
}
