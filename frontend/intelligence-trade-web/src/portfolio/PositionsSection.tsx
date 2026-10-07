import { useId, type FormEvent, type ReactNode } from 'react';
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
  onViewChange,
  onStateChange,
  onSideChange,
  onSymbolChange,
  onLoadMore,
}: PositionsSectionProps) {
  const titleId = useId();
  const idPrefix = useId();

  function handleSymbolSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const symbol = String(new FormData(event.currentTarget).get('symbol') ?? '').trim();
    onSymbolChange(symbol.length === 0 ? null : symbol);
  }

  return (
    <section className="positions-section" aria-labelledby={titleId}>
      <h2 id={titleId} className="section-title">
        Позиции
      </h2>
      <div className="segmented" role="group" aria-label="Режим списка позиций">
        <button
          type="button"
          className="segmented-option"
          aria-pressed={filters.view === 'current'}
          onClick={() => onViewChange('current')}
        >
          Текущие
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

      <div className="positions-filters">
        {filters.view === 'current' && (
          <div className="field">
            <label htmlFor={`${idPrefix}-state`}>Состояние</label>
            <select
              id={`${idPrefix}-state`}
              value={filters.state ?? ''}
              onChange={(event) => onStateChange((event.target.value || null) as CurrentTrackingState | null)}
            >
              {STATE_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
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
            {SIDE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
        <form key={filters.symbol ?? ''} className="symbol-filter" role="search" onSubmit={handleSymbolSubmit}>
          <div className="field">
            <label htmlFor={`${idPrefix}-symbol`}>Символ</label>
            <input
              id={`${idPrefix}-symbol`}
              name="symbol"
              type="text"
              defaultValue={filters.symbol ?? ''}
              placeholder="BTCUSDT"
              autoComplete="off"
              spellCheck={false}
            />
          </div>
          <div className="form-actions">
            <button type="submit" className="button button-secondary">
              Найти
            </button>
            {filters.symbol !== null && (
              <button type="button" className="button button-secondary" onClick={() => onSymbolChange(null)}>
                Сбросить
              </button>
            )}
          </div>
        </form>
      </div>

      {list.loading && (
        <p className="inline-status" role="status">
          <span className="spinner" aria-hidden="true" />
          Загружаем позиции…
        </p>
      )}
      {!list.loading && list.items.length === 0 && list.error === null && (
        <p className="muted">{filters.view === 'closed' ? 'Закрытых позиций нет.' : 'Текущих позиций нет.'}</p>
      )}
      {list.items.length > 0 && (
        <ul className="position-list">
          {list.items.map((position) => (
            <li key={position.id}>
              <PositionCard position={position} />
            </li>
          ))}
        </ul>
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

function PositionCard({ position }: { position: PositionListItem }) {
  const titleId = useId();
  const asset = position.settlementAsset;

  return (
    <article className="panel position-card" aria-labelledby={titleId}>
      <header className="position-header">
        <h3 id={titleId} className="position-title">
          {position.symbol} · {formatNumber(position.size)} {SIDE_LABELS[position.side]}
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
    </article>
  );
}
