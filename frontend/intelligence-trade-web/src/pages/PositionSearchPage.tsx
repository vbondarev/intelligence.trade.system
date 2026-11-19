import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import { Link, useLocation, useSearchParams } from 'react-router-dom';
import { listConnections } from '../connections/connectionsApi';
import { PositionCards } from '../portfolio/PositionsSection';
import { listPositions, PortfolioApiError } from '../portfolio/portfolioApi';
import type { PositionPage } from '../portfolio/portfolioTypes';
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
    setParams(term ? { symbol: term } : {});
    setAttempt((value) => value + 1);
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
  const pagination = useRef<AbortController | null>(null);

  useEffect(() => {
    const scheduler = updates.createReader(async (signal, view) => {
      try {
        const next = await listPositions({ symbol, trackingState: closed ? 'closed' : undefined }, signal);
        if (!signal.aborted) view.apply(() => {
          pagination.current?.abort();
          pagination.current = null;
          setLoadingMore(false);
          setError('');
          setResult(next);
        });
      } catch (failure) {
        if (!signal.aborted) view.fail(failure, () => setError(searchError(failure)));
      }
    });
    reader.current = scheduler;
    scheduler.refresh();
    return () => {
      reader.current = null;
      scheduler.dispose();
      pagination.current?.abort();
      pagination.current = null;
    };
  }, [closed, symbol, updates]);

  useRealtimeInvalidation((event) => {
    if (event.type === 'recovery') reader.current?.recover();
    if (event.type === 'position.updated') reader.current?.invalidate();
  });

  async function loadMore() {
    if (!result?.nextCursor || pagination.current) return;
    const request = new AbortController();
    pagination.current = request;
    setLoadingMore(true);
    setError('');
    try {
      const next = await listPositions({
        symbol, trackingState: closed ? 'closed' : undefined, cursor: result.nextCursor,
      }, request.signal);
      if (!request.signal.aborted) {
        const ids = new Set(result.items.map(({ id }) => id));
        setResult({ ...next, items: [...result.items, ...next.items.filter(({ id }) => !ids.has(id))] });
      }
    } catch (failure) {
      if (!request.signal.aborted) setError(searchError(failure));
    } finally {
      if (pagination.current === request) {
        pagination.current = null;
        setLoadingMore(false);
      }
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

function searchError(error: unknown) {
  if (error instanceof PortfolioApiError && (error.status === 401 || error.status === 403)) {
    return 'Доступ к поиску недоступен. Проверьте, что вы вошли в приложение.';
  }
  return 'Не удалось загрузить позиции. Повторите запрос.';
}
