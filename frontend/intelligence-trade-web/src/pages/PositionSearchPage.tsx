import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { listConnections } from '../connections/connectionsApi';
import { PositionCards } from '../portfolio/PositionsSection';
import { listPositions, PortfolioApiError } from '../portfolio/portfolioApi';
import type { PositionListQuery, PositionPage } from '../portfolio/portfolioTypes';
import type { ScreenReader } from '../realtime/autoUpdates';
import { useAutoUpdates } from '../realtime/autoUpdatesContext';
import { useRealtimeInvalidation } from '../realtime/useRealtimeInvalidation';

export function PositionSearchPage() {
  const [params, setParams] = useSearchParams();
  const [attempt, setAttempt] = useState(0);
  const symbol = params.get('symbol')?.trim().slice(0, 100) ?? '';
  const id = useId();

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const term = String(new FormData(event.currentTarget).get('symbol') ?? '').trim();
    if (term === (params.get('symbol') ?? '')) {
      setAttempt((value) => value + 1);
    } else {
      setParams(term ? { symbol: term } : {});
    }
  }

  return (
    <div className="positions-section">
      <h1>Навигация и поиск позиций</h1>
      <nav aria-label="Разделы приложения">
        <ul>
          <li><Link to="/app">Обзор</Link></li>
          <li><Link to="/app/settings/connections">Подключения</Link></li>
        </ul>
      </nav>
      <form key={symbol} role="search" className="symbol-filter" onSubmit={submit}>
        <div className="field">
          <label htmlFor={id}>Символ</label>
          <input id={id} name="symbol" defaultValue={symbol} placeholder="BTCUSDT" maxLength={100} required />
        </div>
        <button className="button button-secondary">Найти</button>
      </form>
      {symbol && <SearchResults key={JSON.stringify([symbol, attempt])} symbol={symbol} />}
    </div>
  );
}

function SearchResults({ symbol }: { symbol: string }) {
  const [accounts, setAccounts] = useState<ReadonlyMap<string, string>>(() => new Map());
  const updates = useAutoUpdates();
  const reader = useRef<ScreenReader | null>(null);
  useEffect(() => {
    const scheduler = updates.createReader(async (signal, view) => {
      try {
        const connections = await listConnections(signal);
        if (!signal.aborted) view.apply(() => setAccounts(new Map(connections.map(({ id, displayName }) => [id, displayName]))));
      } catch (error) {
        if (!signal.aborted) view.fail(error, () => {});
      }
    });
    reader.current = scheduler;
    scheduler.refresh();
    return () => { reader.current = null; scheduler.dispose(); };
  }, [updates]);
  useRealtimeInvalidation((event) => {
    if (event.type === 'recovery') reader.current?.recover();
    if (event.type === 'exchangeAccount.updated') reader.current?.invalidate();
  });
  return (
    <>
      <ResultGroup symbol={symbol} accounts={accounts} closed={false} />
      <ResultGroup symbol={symbol} accounts={accounts} closed />
    </>
  );
}

function ResultGroup({ symbol, accounts, closed }: {
  symbol: string;
  accounts: ReadonlyMap<string, string>;
  closed: boolean;
}) {
  const updates = useAutoUpdates();
  const location = useLocation();
  const id = useId();
  const [result, setResult] = useState<PositionPage | null>(null);
  const [error, setError] = useState('');
  const [loadingMore, setLoadingMore] = useState(false);
  const reader = useRef<ScreenReader | null>(null);
  const pagination = useRef<{ controller: AbortController; settled: Promise<boolean> } | null>(null);
  const snapshot = useRef<SearchSnapshot | null>(null);
  const deferred = useRef<'invalidate' | 'recover' | null>(null);

  useEffect(() => {
    const scheduler = updates.createReader(async (signal, view) => {
      const pendingPage = pagination.current;
      const keepManualError = view.kind === 'resume' && pendingPage !== null
        ? await waitForManualPage(pendingPage.settled, signal)
        : false;
      if (signal.aborted) return;
      const startingSnapshot = snapshot.current;
      const manualAtStart = pagination.current !== null;
      function deferLiveCommit() {
        if (view.kind !== 'live') return false;
        if (!manualAtStart && !pagination.current && snapshot.current === startingSnapshot) return false;
        deferred.current = 'recover';
        // Если ручной запрос уже завершился, перечитываем сразу через текущий scheduler.
        if (!pagination.current) {
          deferred.current = null;
          reader.current?.recover();
        }
        return true;
      }
      try {
        const query = { symbol, trackingState: closed ? 'closed' as const : undefined };
        const pageCount = view.kind === 'silent' ? 1 : startingSnapshot?.pageCount ?? 1;
        const next = await readSearchSnapshot(query, pageCount, signal);
        if (!signal.aborted) view.apply(() => {
          if (deferLiveCommit()) return;
          pagination.current?.controller.abort();
          pagination.current = null;
          deferred.current = null;
          snapshot.current = next;
          setLoadingMore(false);
          if (!keepManualError) setError('');
          setResult(next.page);
        });
      } catch (failure) {
        if (!signal.aborted) view.fail(failure, () => {
          if (!deferLiveCommit()) setError(searchError(failure));
        });
      }
    });
    reader.current = scheduler;
    scheduler.refresh();
    return () => {
      reader.current = null;
      scheduler.dispose();
      pagination.current?.controller.abort();
      pagination.current = null;
      deferred.current = null;
    };
  }, [closed, symbol, updates]);

  useRealtimeInvalidation((event) => {
    if (event.type !== 'recovery' && event.type !== 'position.updated') return;
    const action = event.type === 'recovery' ? 'recover' : 'invalidate';
    if (updates.isLive() && pagination.current) {
      deferred.current = deferred.current === 'recover' ? 'recover' : action;
    } else {
      reader.current?.[action]();
    }
  });

  async function loadMore() {
    const current = snapshot.current;
    const cursor = current?.page.nextCursor;
    if (!current || !cursor || pagination.current) return;
    updates.cancelResume();
    const request = new AbortController();
    let finish!: (failed: boolean) => void;
    const settled = new Promise<boolean>((resolve) => { finish = resolve; });
    pagination.current = { controller: request, settled };
    let failed = false;
    setLoadingMore(true);
    setError('');
    try {
      const next = await listPositions({
        symbol, trackingState: closed ? 'closed' : undefined, cursor,
      }, request.signal);
      if (!request.signal.aborted && snapshot.current === current) {
        const appended = appendSearchPage(current, cursor, next);
        snapshot.current = appended;
        setResult(appended.page);
      }
    } catch (failure) {
      if (!request.signal.aborted) {
        failed = true;
        setError(searchError(failure));
      }
    } finally {
      if (pagination.current?.controller === request) {
        pagination.current = null;
        setLoadingMore(false);
        const action = deferred.current;
        deferred.current = null;
        if (action) reader.current?.[action]();
      }
      finish(failed);
    }
  }

  return (
    <section aria-labelledby={id}>
      <h2 id={id}>{closed ? 'Закрытые позиции' : 'Текущие позиции'}</h2>
      {!result && !error && <p role="status">Загружаем позиции…</p>}
      {result?.items.length === 0 && <p>Позиции с таким символом не найдены.</p>}
      {!!result?.items.length && (
        <PositionCards items={result.items} accounts={accounts} returnTo={location.pathname + location.search} />
      )}
      {error && (
        <div role="alert">
          <p>{error}</p>
          {result === null && <button type="button" className="button button-secondary" onClick={() => reader.current?.refresh()}>Повторить поиск</button>}
        </div>
      )}
      {result?.hasMore && result.nextCursor && (
        <button type="button" className="button button-secondary" disabled={loadingMore} onClick={() => void loadMore()}>
          {loadingMore ? 'Загружаем…' : 'Загрузить ещё'}
        </button>
      )}
    </section>
  );
}

async function waitForManualPage(settled: Promise<boolean>, signal: AbortSignal): Promise<boolean> {
  if (signal.aborted) return false;
  let abort!: () => void;
  const cancelled = new Promise<boolean>((resolve) => { abort = () => resolve(false); });
  signal.addEventListener('abort', abort, { once: true });
  try {
    return await Promise.race([settled, cancelled]);
  } finally {
    signal.removeEventListener('abort', abort);
  }
}

interface SearchSnapshot {
  page: PositionPage;
  pageCount: number;
  usedCursors: ReadonlySet<string>;
}

function appendSearchPage(current: SearchSnapshot, cursor: string, next: PositionPage): SearchSnapshot {
  const usedCursors = new Set(current.usedCursors).add(cursor);
  const ids = new Set(current.page.items.map(({ id }) => id));
  const items = [...current.page.items];
  for (const item of next.items) {
    if (ids.has(item.id)) continue;
    ids.add(item.id);
    items.push(item);
  }
  const nextCursor = next.hasMore && next.nextCursor && !usedCursors.has(next.nextCursor)
    ? next.nextCursor
    : null;
  return { page: { items, nextCursor, hasMore: nextCursor !== null }, pageCount: current.pageCount + 1, usedCursors };
}

async function readSearchSnapshot(query: PositionListQuery, pageCount: number, signal: AbortSignal): Promise<SearchSnapshot> {
  let result: SearchSnapshot = { page: await listPositions(query, signal), pageCount: 1, usedCursors: new Set() };
  while (result.pageCount < pageCount && result.page.hasMore && result.page.nextCursor) {
    signal.throwIfAborted();
    const cursor = result.page.nextCursor;
    result = appendSearchPage(result, cursor, await listPositions({ ...query, cursor }, signal));
  }
  return result;
}

function searchError(error: unknown) {
  if (error instanceof PortfolioApiError && (error.status === 401 || error.status === 403)) {
    return 'Доступ к поиску недоступен. Проверьте, что вы вошли в приложение.';
  }
  return 'Не удалось загрузить позиции. Повторите запрос.';
}
