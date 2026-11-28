import { StatusMessage } from '../ui/StatusMessage';
import { useId, type FormEvent, type MouseEvent, type ReactNode, type Ref } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { MarketValuationDetails } from '../freshness/FreshnessDetails';
import { positionPagePath, type PositionPageState } from '../position/positionNavigation';
import type {
  CurrentTrackingState,
  PositionFilters,
  PositionListItem,
  PositionSide,
  PositionsView,
} from './portfolioTypes';
import {
  SIDE_LABELS,
  TRACKING_STATE_LABELS,
  formatAssetAmount,
  formatLeverage,
  formatNumber,
  formatSignedAssetAmount,
  formatTimestamp,
  pnlClassName,
} from './portfolioFormatting';

const STATE_OPTIONS: readonly { value: CurrentTrackingState | ''; label: string }[] = [
  { value: '', label: 'Все' },
  { value: 'active', label: 'Отслеживаются' },
  { value: 'unknown', label: 'Не подтверждены' },
  { value: 'stale', label: 'Устарели' },
];

const SIDE_OPTIONS: readonly { value: PositionSide | ''; label: string }[] = [
  { value: '', label: 'Все' },
  { value: 'long', label: 'Long' },
  { value: 'short', label: 'Short' },
];

export interface PositionsListState {
  loading: boolean;
  items: readonly PositionListItem[];
  hasMore: boolean;
  loadingMore: boolean;
  error: ReactNode;
}

interface PositionsSectionProps {
  filters: PositionFilters;
  list: PositionsListState;
  accountName?: string;
  activePositionId?: string;
  currentPositionCount?: number;
  previewId: string;
  headingRef: Ref<HTMLHeadingElement>;
  onOpenPosition: (position: PositionListItem, offset: number, keyboard: boolean) => void;
  onViewChange: (view: PositionsView) => void;
  onStateChange: (state: CurrentTrackingState | null) => void;
  onSideChange: (side: PositionSide | null) => void;
  onSymbolChange: (symbol: string | null) => void;
  onLoadMore: () => void;
}

/**
 * Список позиций выбранного подключения. Фильтрацию выполняет backend: компонент только
 * передаёт выбранные значения, а symbol — exact filter без локального поиска по подстроке.
 */
export function PositionsSection({
  filters,
  list,
  accountName,
  activePositionId,
  currentPositionCount,
  previewId,
  headingRef,
  onOpenPosition,
  onViewChange,
  onStateChange,
  onSideChange,
  onSymbolChange,
  onLoadMore,
}: PositionsSectionProps) {
  const titleId = useId();
  const idPrefix = useId();
  const location = useLocation();
  const returnTo = `${location.pathname}${location.search}`;

  function handleSymbolSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const symbol = String(new FormData(event.currentTarget).get('symbol') ?? '').trim();
    onSymbolChange(symbol.length === 0 ? null : symbol);
  }

  return (
    <section className="panel positions-section" aria-labelledby={titleId}>
      <header className="overview-list-header">
        <h2 id={titleId} className="section-title" ref={headingRef} tabIndex={-1}>
          Позиции
        </h2>
        <div className="segmented" role="group" aria-label="Режим списка позиций">
          <button
            type="button"
            className="segmented-option"
            aria-pressed={filters.view === 'current'}
            aria-label={currentPositionCount === undefined ? undefined : 'Открытые ' + currentPositionCount}
            onClick={() => onViewChange('current')}
          >
            Открытые{currentPositionCount !== undefined && <span className="overview-position-count"> {currentPositionCount}</span>}
          </button>
          <button
            type="button"
            className="segmented-option"
            aria-pressed={filters.view === 'closed'}
            onClick={() => onViewChange('closed')}
          >
            Закрытые
          </button>
        </div>
      </header>

      <div className="overview-filters">
        <form key={filters.symbol ?? ''} className="symbol-filter" role="search" onSubmit={handleSymbolSubmit}>
          <label htmlFor={`${idPrefix}-symbol`} className="screen-reader-only">Символ</label>
          <div className="overview-search">
            <input
              id={`${idPrefix}-symbol`}
              name="symbol"
              type="text"
              defaultValue={filters.symbol ?? ''}
              placeholder="Найти инструмент"
              autoComplete="off"
              spellCheck={false}
            />
            <button type="submit" className="button button-secondary" aria-label="Найти">
              <svg aria-hidden="true" viewBox="0 0 20 20" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="1.5">
                <circle cx="8.5" cy="8.5" r="5.5" />
                <path d="m13 13 4 4" />
              </svg>
            </button>
          </div>
          {filters.symbol !== null && (
            <button type="button" className="button button-secondary" onClick={() => onSymbolChange(null)}>
              Сбросить
            </button>
          )}
        </form>
        <details className="overview-disclosure overview-advanced-filters">
          <summary>Фильтры{(filters.state || filters.side) && <span> · заданы</span>}</summary>
          <div className="positions-filters">
            {filters.view === 'current' && (
              <div className="field">
                <label htmlFor={`${idPrefix}-state`}>Состояние</label>
                <select
                  id={`${idPrefix}-state`}
                  value={filters.state ?? ''}
                  onChange={(event) => onStateChange((event.target.value || null) as CurrentTrackingState | null)}
                >
                  {STATE_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
                </select>
              </div>
            )}
            <div className="field">
              <label htmlFor={`${idPrefix}-side`}>Направление</label>
              <select
                id={`${idPrefix}-side`}
                value={filters.side ?? ''}
                onChange={(event) => onSideChange((event.target.value || null) as PositionSide | null)}
              >
                {SIDE_OPTIONS.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
            </div>
          </div>
        </details>
      </div>

      {list.loading && (
        <StatusMessage kind="loading" text="Загружаем позиции…" />
      )}
      {!list.loading && list.items.length === 0 && list.error === null && (
        <StatusMessage kind="empty" text={filters.view === 'closed' ? 'Закрытых позиций нет.' : 'Текущих позиций нет.'} />
      )}
      {list.items.length > 0 && (
        <table className="overview-positions" aria-labelledby={titleId}>
          <thead>
            <tr>
              <th scope="col">Инструмент</th>
              <th scope="col">Объём</th>
              <th scope="col">PnL</th>
            </tr>
          </thead>
          <tbody>
            {list.items.map((position, index) => {
              const selected = position.id === activePositionId;
              const duplicate = list.items.some((other) => other.id !== position.id && positionLabel(other) === positionLabel(position));
              const context = duplicate ? positionLinkContext(position, accountName, index) : null;
              const activate = (event: MouseEvent<HTMLTableRowElement>) => {
                if (event.defaultPrevented || event.button !== 0 || event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
                onOpenPosition(position, event.currentTarget.getBoundingClientRect().top, event.detail === 0);
              };

              return (
                <tr
                  key={position.id}
                  data-position-id={position.id}
                  className={selected ? 'overview-position-selected' : undefined}
                  onClick={activate}
                >
                  <th scope="row">
                    <Link
                      className="position-link overview-ticker"
                      aria-label={[positionLabel(position), context].filter(Boolean).join(' — ')}
                      aria-current={selected ? 'true' : undefined}
                      aria-controls={selected ? previewId : undefined}
                      to={positionPagePath(position.id)}
                      state={{ returnTo } satisfies PositionPageState}
                      onClick={(event) => {
                        if (event.button === 0 && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
                          event.preventDefault();
                          const row = event.currentTarget.closest('tr');
                          if (row) onOpenPosition(position, row.getBoundingClientRect().top, event.detail === 0);
                        }
                      }}
                    >
                      {position.symbol}
                    </Link>
                    {selected && (
                      <span className="overview-open-indicator">
                        {position.trackingState === 'closed'
                          ? 'Карточка открыта'
                          : <><span className="screen-reader-only">Карточка </span>Открыта</>}
                      </span>
                    )}
                    <span className="overview-position-side">{SIDE_LABELS[position.side]}</span>
                    {context && <span className="position-link-context">{context}</span>}
                    {position.trackingState !== 'active' && (
                      <span className={`tracking-state tracking-state-${position.trackingState}`}>
                        {TRACKING_STATE_LABELS[position.trackingState]}
                      </span>
                    )}
                  </th>
                  <td>{formatAssetAmount(position.positionValue, position.settlementAsset)}</td>
                  <td className={pnlClassName(position.unrealizedPnl)}>
                    {formatSignedAssetAmount(position.unrealizedPnl, position.settlementAsset)}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      )}
      {list.error}
      {!list.loading && list.hasMore && (
        <div className="form-actions">
          <button type="button" className="button button-secondary" disabled={list.loadingMore} onClick={onLoadMore}>
            {list.loadingMore ? 'Загружаем…' : 'Загрузить ещё'}
          </button>
        </div>
      )}
    </section>
  );
}

function positionLabel(position: PositionListItem) {
  return `${position.symbol} · ${formatNumber(position.size)} ${SIDE_LABELS[position.side]}`;
}

function positionLinkContext(position: PositionListItem, account: string | undefined, duplicateIndex?: number) {
  return [
    account, position.settlementAsset, `вход ${formatNumber(position.averageEntryPrice)}`,
    formatTimestamp(position.closedAt ?? position.firstDetectedAt),
    duplicateIndex === undefined ? null : `позиция ${duplicateIndex + 1}`,
  ].filter(Boolean).join(' · ');
}

export function PositionCards({ items, returnTo, accounts, accountName }: {
  items: readonly PositionListItem[];
  returnTo: string;
  accounts?: ReadonlyMap<string, string>;
  accountName?: string;
}) {
  const counts = new Map<string, number>();
  for (const item of items) {
    const label = positionLabel(item);
    counts.set(label, (counts.get(label) ?? 0) + 1);
  }

  return (
    <ul className="position-list">
      {items.map((position, index) => {
        const duplicate = (counts.get(positionLabel(position)) ?? 0) > 1;
        const account = accounts?.get(position.exchangeAccountId) ?? accountName;
        const context = accounts || duplicate
          ? positionLinkContext(position, account, duplicate ? index : undefined)
          : null;
        return (
          <li key={position.id}>
            <PositionCard position={position} returnTo={returnTo} context={context} />
          </li>
        );
      })}
    </ul>
  );
}

function PositionCard({ position, returnTo, context }: { position: PositionListItem; returnTo: string; context: string | null }) {
  const titleId = useId();
  const labelId = `${titleId}-label`;
  const contextId = `${titleId}-context`;
  const asset = position.settlementAsset;
  const state: PositionPageState = { returnTo };

  return (
    <article className="panel position-card" aria-labelledby={titleId}>
      <header className="position-header">
        <h3 id={titleId} className="position-title">
          <Link className="position-link" to={positionPagePath(position.id)} state={state}
            aria-labelledby={context ? `${labelId} ${contextId}` : undefined}>
            <span id={labelId}>{positionLabel(position)}</span>
            {context && <span id={contextId} className="position-link-context">— {context}</span>}
          </Link>
        </h3>
        {position.trackingState !== 'active' && (
          <span className={`tracking-state tracking-state-${position.trackingState}`}>
            {TRACKING_STATE_LABELS[position.trackingState]}
          </span>
        )}
      </header>
      <dl className="value-rows">
        <div>
          <dt>Вход</dt>
          <dd>{formatNumber(position.averageEntryPrice)}</dd>
        </div>
        <div>
          <dt>Mark</dt>
          <dd>{formatNumber(position.markPrice)}</dd>
        </div>
        <div>
          <dt>Стоимость</dt>
          <dd>{formatAssetAmount(position.positionValue, asset)}</dd>
        </div>
        <div>
          <dt>PnL</dt>
          <dd>{formatSignedAssetAmount(position.unrealizedPnl, asset)}</dd>
        </div>
        <div>
          <dt>Плечо</dt>
          <dd>{formatLeverage(position.leverage)}</dd>
        </div>
        {position.closedAt !== null && (
          <div>
            <dt>Закрыта</dt>
            <dd>
              <time dateTime={position.closedAt}>{formatTimestamp(position.closedAt)}</time>
            </dd>
          </div>
        )}
      </dl>
      <MarketValuationDetails valuation={position.marketValuation} />
    </article>
  );
}
