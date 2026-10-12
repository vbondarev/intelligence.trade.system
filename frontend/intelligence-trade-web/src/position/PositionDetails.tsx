import { useId } from 'react';
import { Link } from 'react-router-dom';
import {
  SIDE_LABELS,
  TRACKING_STATE_LABELS,
  formatAssetAmount,
  formatLeverage,
  formatNumber,
  formatSignedAssetAmount,
  formatTimestamp,
} from '../portfolio/portfolioFormatting';
import { MARKET_CATEGORY_LABELS, formatPrice } from './positionFormatting';
import { accountOverviewPath } from './positionNavigation';
import type { PositionDetails as Position } from './positionTypes';

function signClass(value: number | null): string | undefined {
  if (value === null || value === 0) {
    return undefined;
  }

  return value > 0 ? 'value-positive' : 'value-negative';
}

/**
 * Сохранённое состояние позиции. Стоимость и PnL подписаны фактическим `settlementAsset`;
 * отсутствующие уровни показываются прочерком, а не нулём.
 */
export function PositionDetails({ position }: { position: Position }) {
  const titleId = useId();
  const asset = position.settlementAsset;

  return (
    <section className="panel position-details" aria-labelledby={titleId}>
      <header className="position-header">
        <h2 id={titleId} className="position-title">
          Параметры позиции
        </h2>
        <span className={`tracking-state tracking-state-${position.trackingState}`}>
          {TRACKING_STATE_LABELS[position.trackingState]}
        </span>
      </header>
      <div className="position-details-grid">
        <dl className="value-rows">
          <div>
            <dt>Подключение</dt>
            <dd>
              <Link to={accountOverviewPath(position.exchangeAccountId)}>Обзор подключения</Link>
            </dd>
          </div>
          <div>
            <dt>Рынок</dt>
            <dd>{MARKET_CATEGORY_LABELS[position.marketCategory]}</dd>
          </div>
          <div>
            <dt>Направление</dt>
            <dd>{SIDE_LABELS[position.side]}</dd>
          </div>
          <div>
            <dt>Размер</dt>
            <dd>{formatNumber(position.size)}</dd>
          </div>
          <div>
            <dt>Средняя цена входа</dt>
            <dd>{formatPrice(position.averageEntryPrice)}</dd>
          </div>
          <div>
            <dt>Mark (сохранённая)</dt>
            <dd>{formatPrice(position.markPrice)}</dd>
          </div>
          <div>
            <dt>Стоимость</dt>
            <dd>{formatAssetAmount(position.positionValue, asset)}</dd>
          </div>
          <div>
            <dt>Нереализованный PnL</dt>
            <dd className={signClass(position.unrealizedPnl)}>{formatSignedAssetAmount(position.unrealizedPnl, asset)}</dd>
          </div>
          <div>
            <dt>Актив расчёта</dt>
            <dd>{asset}</dd>
          </div>
        </dl>
        <dl className="value-rows">
          <div>
            <dt>Плечо</dt>
            <dd>{formatLeverage(position.leverage)}</dd>
          </div>
          <div>
            <dt>Цена ликвидации</dt>
            <dd>{formatPrice(position.liquidationPrice)}</dd>
          </div>
          <div>
            <dt>Безубыток</dt>
            <dd>{formatPrice(position.breakEvenPrice)}</dd>
          </div>
          <div>
            <dt>Take Profit</dt>
            <dd>{formatPrice(position.takeProfit)}</dd>
          </div>
          <div>
            <dt>Stop Loss</dt>
            <dd>{formatPrice(position.stopLoss)}</dd>
          </div>
          <div>
            <dt>Trailing Stop</dt>
            <dd>{formatPrice(position.trailingStop)}</dd>
          </div>
          <div>
            <dt>Последнее наблюдение</dt>
            <dd>
              <time dateTime={position.lastObservedAt}>{formatTimestamp(position.lastObservedAt)}</time>
            </dd>
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
      </div>
    </section>
  );
}
