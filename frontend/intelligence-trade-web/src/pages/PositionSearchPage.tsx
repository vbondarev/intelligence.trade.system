import { presentError, type ErrorPresentation } from '../errors/errorPresentation';
import { StatusMessage } from '../ui/StatusMessage';
import { useAuth } from '../auth/authContext';
import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { listConnections } from '../connections/connectionsApi';
import { PositionCards } from '../portfolio/PositionsSection';
import { listPositions } from '../portfolio/portfolioApi';
import type { PositionListQuery, PositionPage } from '../portfolio/portfolioTypes';
import { resourceUnavailable, type ScreenReader } from '../realtime/autoUpdates';
import { useAutoUpdates } from '../realtime/autoUpdatesContext';
import { useRealtimeInvalidation } from '../realtime/useRealtimeInvalidation';

export function PositionSearchPage() {
  const [params, setParams] = useSearchParams();
  const [attempt, setAttempt] = useState(0);
  const symbol = params.get('symbol')?.trim().slice(0, 100) ?? '';
  const [draft, setDraft] = useState({ symbol, value: symbol });
  const id = useId();

  if (draft.symbol !== symbol) setDraft({ symbol, value: symbol });

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
      <form role="search" className="symbol-filter" onSubmit={submit}>
        <div className="field">
          <label htmlFor={id}>Символ</label>
          <input
            id={id}
            name="symbol"
            value={draft.value}
            onChange={(event) => setDraft({ symbol, value: event.target.value })}
            placeholder="BTCUSDT"
            maxLength={100}
            required
          />
        </div>
        <button className="button button-secondary">Найти</button>
      </form>
      {symbol && <SearchResults key={JSON.stringify([symbol, attempt])} symbol={symbol} />}
    </div>
  );
}

function SearchResults({ symbol }: { symbol: string }) {
  const [accounts, setAccounts] = useState<ReadonlyMap<string, string>>(() => new Map());
  const [accountError, setAccountError] = useState<{ notice: ErrorPresentation; background: boolean } | null>(null);
  const updates = useAutoUpdates();
  const reader = useRef<ScreenReader | null>(null);
  const accountsGeneration = useRef(0);
  useEffect(() => {
    const scheduler = updates.createReader(async (signal, view) => {
      const generation = accountsGeneration.current;
      const isCurrent = () => !signal.aborted && accountsGeneration.current === generation;
      try {
        const connections = await listConnections(signal);
        view.apply(() => {
          setAccounts(new Map(connections.map(({ id, displayName }) => [id, displayName])));
          setAccountError(null);
        }, isCurrent);
      } catch (error) {
        const notice = presentError(error, { fallback: 'Не удалось загрузить названия подключений.', retry: true });
        view.fail(error, () => {
          if (isCurrent()) setAccountError({ notice, background: view.kind === 'live' });
        }, () => {
          accountsGeneration.current += 1;
          setAccounts(new Map());
          if (view.kind !== 'silent') setAccountError({ notice, background: false });
        }, isCurrent);
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
      {accountError && (
        <StatusMessage kind={accountError.background ? 'warning' : 'error'} text={accountError.notice.text}
          traceId={accountError.notice.traceId} actions={accountError.notice.action === 'retry' &&
            <button type="button" className="button button-secondary" onClick={() => reader.current?.refresh()}>Обновить подключения</button>
          } />
      )}
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
  const { login, captureSessionEnd } = useAuth();
  const id = useId();
  const [result, setResult] = useState<PositionPage | null>(null);
  const [error, setError] = useState<ErrorPresentation | null>(null);
  const [backgroundError, setBackgroundError] = useState(false);
  const [loadingMore, setLoadingMore] = useState(false);
  const reader = useRef<ScreenReader | null>(null);
  const pagination = useRef<{ controller: AbortController; settled: Promise<boolean> } | null>(null);
  const snapshot = useRef<SearchSnapshot | null>(null);
  const resultGeneration = useRef(0);
  const deferred = useRef<'invalidate' | 'recover' | null>(null);

  useEffect(() => {
    const scheduler = updates.createReader(async (signal, view) => {
      const pendingPage = pagination.current;
      const keepManualError = view.kind === 'resume' && pendingPage !== null
        ? await waitForManualPage(pendingPage.settled, signal)
        : false;
      if (signal.aborted) return;
      const generation = resultGeneration.current;
      const startingSnapshot = snapshot.current;
      const manualAtStart = pagination.current !== null;
      const isSessionCurrent = view.beginRequest?.();
      const isCurrent = () => !signal.aborted && resultGeneration.current === generation && isSessionCurrent?.() !== false;
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
        const next = await readSearchSnapshot(query, pageCount, signal, isCurrent);
        if (next && isCurrent()) view.apply(() => {
          if (deferLiveCommit()) return;
          pagination.current?.controller.abort();
          pagination.current = null;
          deferred.current = null;
          snapshot.current = next;
          setLoadingMore(false);
          if (!keepManualError) {
            setError(null);
            setBackgroundError(false);
          }
          setResult(next.page);
        }, isCurrent);
      } catch (failure) {
        const notice = searchError(failure);
        if (isCurrent()) view.fail(failure, () => {
          if (isCurrent() && !deferLiveCommit()) {
            setError(notice);
            setBackgroundError(view.kind === 'live' && snapshot.current !== null);
          }
        }, () => {
          resultGeneration.current += 1;
          pagination.current?.controller.abort();
          pagination.current = null;
          snapshot.current = null;
          deferred.current = null;
          setLoadingMore(false);
          setResult(null);
          setBackgroundError(false);
          if (view.kind !== 'silent') setError(notice);
        }, isCurrent);
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
    const endSession = captureSessionEnd();
    const current = snapshot.current;
    const cursor = current?.page.nextCursor;
    if (!current || !cursor || pagination.current) return;
    updates.cancelResume();
    const request = new AbortController();
    const generation = resultGeneration.current;
    const isCurrent = () => !request.signal.aborted && resultGeneration.current === generation && snapshot.current === current;
    let finish!: (failed: boolean) => void;
    const settled = new Promise<boolean>((resolve) => { finish = resolve; });
    pagination.current = { controller: request, settled };
    let failed = false;
    setLoadingMore(true);
    setError(null);
    setBackgroundError(false);
    try {
      const next = await listPositions({
        symbol, trackingState: closed ? 'closed' : undefined, cursor,
      }, request.signal);
      if (isCurrent()) {
        const appended = appendSearchPage(current, cursor, next);
        snapshot.current = appended;
        setResult(appended.page);
      }
    } catch (failure) {
      if (isCurrent()) {
        const revoked = resourceUnavailable(failure);
        failed = !revoked;
        const notice = searchError(failure);
        if (notice.action === 'login') endSession(notice.traceId);
        else updates.reportAccessFailure(failure);
        if (revoked) {
          resultGeneration.current += 1;
          snapshot.current = null;
          deferred.current = null;
          setResult(null);
        }
        setError(notice);
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
      {!result && !error && <StatusMessage kind="loading" text="Загружаем позиции…" />}
      {result?.items.length === 0 && <StatusMessage kind="empty" text="Позиции с таким символом не найдены." />}
      {!!result?.items.length && (
        <PositionCards items={result.items} accounts={accounts} returnTo={location.pathname + location.search} />
      )}
      {error && (
        <StatusMessage kind={backgroundError ? 'warning' : 'error'} text={error.text} traceId={error.traceId} actions={
          error.action === 'login' ? <button type="button" className="button button-secondary" onClick={login}>Войти снова</button> :
          (result === null || backgroundError) && error.action === 'retry' && (
            <button type="button" className="button button-secondary" onClick={() => reader.current?.refresh()}>Повторить поиск</button>
          )
        } />
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

async function readSearchSnapshot(
  query: PositionListQuery,
  pageCount: number,
  signal: AbortSignal,
  isCurrent: () => boolean,
): Promise<SearchSnapshot | null> {
  let result: SearchSnapshot = { page: await listPositions(query, signal), pageCount: 1, usedCursors: new Set() };
  while (result.pageCount < pageCount && result.page.hasMore && result.page.nextCursor) {
    if (!isCurrent()) return null;
    const cursor = result.page.nextCursor;
    result = appendSearchPage(result, cursor, await listPositions({ ...query, cursor }, signal));
  }
  return result;
}

function searchError(error: unknown): ErrorPresentation {
  return presentError(error, {
    fallback: 'Не удалось загрузить позиции. Повторите запрос.',
    retry: true,
    messages: { unavailable: 'Не удалось загрузить позиции. Повторите запрос.', forbidden: 'Доступ к поиску недоступен. Проверьте, что вы вошли в приложение.' },
  });
}
