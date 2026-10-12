import { useId } from 'react';
import { MISSING_VALUE, formatTimestamp } from '../portfolio/portfolioFormatting';
import {
  MARKET_REGIME_LABELS,
  MARKET_TREND_LABELS,
  formatCompact,
  formatFraction,
  formatFundingRate,
  formatPercent,
  formatPrice,
  formatScore,
  formatSignedPercent,
} from './positionFormatting';
import type { MarketTimeframe, PositionMarket } from './positionTypes';

const TIMEFRAMES: readonly { key: 'm15' | 'h1' | 'h4' | 'd1'; label: string }[] = [
  { key: 'm15', label: '15m' },
  { key: 'h1', label: '1h' },
  { key: 'h4', label: '4h' },
  { key: 'd1', label: '1d' },
];

/** Показатель с признаками надёжности backend: ненадёжное или резервное значение помечается. */
function Indicator({ value, reliable, fallback = false }: { value: string; reliable: boolean; fallback?: boolean }) {
  if (value === MISSING_VALUE) {
    return <>{value}</>;
  }

  return (
    <>
      {value}
      {!reliable && <span className="indicator-flag"> (ненадёжно)</span>}
      {reliable && fallback && <span className="indicator-flag"> (резервный расчёт)</span>}
    </>
  );
}

function TimeframeCard({ label, timeframe }: { label: string; timeframe: MarketTimeframe }) {
  return (
    <div className="timeframe-card">
      <h4 className="timeframe-title">{label}</h4>
      <dl className="value-rows">
        <div>
          <dt>Тренд</dt>
          <dd>{MARKET_TREND_LABELS[timeframe.trend]}</dd>
        </div>
        <div>
          <dt>RSI 14</dt>
          <dd>
            <Indicator value={formatScore(timeframe.rsi14)} reliable={timeframe.rsi14IsReliable} />
          </dd>
        </div>
        <div>
          <dt>ATR 14</dt>
          <dd>
            <Indicator
              value={formatPrice(timeframe.atr14)}
              reliable={timeframe.atrIsReliable}
              fallback={timeframe.atrIsFallback}
            />
          </dd>
        </div>
        <div>
          <dt>Отн. объём</dt>
          <dd>
            <Indicator
              value={formatScore(timeframe.volumeRatio)}
              reliable={timeframe.volumeRatioIsReliable}
              fallback={timeframe.volumeRatioIsFallback}
            />
          </dd>
        </div>
        <div>
          <dt>Поддержка</dt>
          <dd>{formatPrice(timeframe.support1)}</dd>
        </div>
        <div>
          <dt>Сопротивление</dt>
          <dd>{formatPrice(timeframe.resistance1)}</dd>
        </div>
      </dl>
    </div>
  );
}

/**
 * Публичный рыночный контекст инструмента. Показатели рассчитаны backend и отображаются как
 * есть; момент снимка `capturedAt` не связан с моментом оценки позиции.
 */
export function PositionMarketContext({ market }: { market: PositionMarket }) {
  const titleId = useId();
  const { price, derivatives, orderBook, tradeFlow, sentiment } = market;
  const change = price.price24hChangePct;

  return (
    <section className="panel market-context" aria-labelledby={titleId}>
      <h2 id={titleId} className="section-title">
        Рыночный контекст
      </h2>
      <p className="muted market-captured">
        Снимок рынка: <time dateTime={market.capturedAt}>{formatTimestamp(market.capturedAt)}</time>
      </p>
      <dl className="metric-grid">
        <div className="metric">
          <dt>Цена</dt>
          <dd>{formatPrice(price.lastPrice)}</dd>
        </div>
        <div className="metric">
          <dt>Изменение 24ч</dt>
          <dd className={change > 0 ? 'value-positive' : change < 0 ? 'value-negative' : undefined}>
            {formatSignedPercent(change)}
          </dd>
        </div>
        <div className="metric">
          <dt>Минимум 24ч</dt>
          <dd>{formatPrice(price.low24h)}</dd>
        </div>
        <div className="metric">
          <dt>Максимум 24ч</dt>
          <dd>{formatPrice(price.high24h)}</dd>
        </div>
      </dl>

      <h3 className="subsection-title">Тренды и индикаторы</h3>
      <div className="timeframe-grid">
        {TIMEFRAMES.map(({ key, label }) => (
          <TimeframeCard key={key} label={label} timeframe={market[key]} />
        ))}
      </div>

      <h3 className="subsection-title">Деривативы</h3>
      <dl className="value-rows">
        <div>
          <dt>Funding</dt>
          <dd>{formatFundingRate(derivatives.fundingRate)}</dd>
        </div>
        {derivatives.nextFundingTime !== null && (
          <div>
            <dt>Следующий funding</dt>
            <dd>
              <time dateTime={derivatives.nextFundingTime}>{formatTimestamp(derivatives.nextFundingTime)}</time>
            </dd>
          </div>
        )}
        <div>
          <dt>Open Interest</dt>
          <dd>{formatCompact(derivatives.openInterest)}</dd>
        </div>
        <div>
          <dt>Open Interest, стоимость</dt>
          <dd>{formatCompact(derivatives.openInterestValue)}</dd>
        </div>
        <div>
          <dt>Изменение OI 1ч / 4ч</dt>
          <dd>
            {formatSignedPercent(derivatives.openInterestChange1hPct)} /{' '}
            {formatSignedPercent(derivatives.openInterestChange4hPct)}
          </dd>
        </div>
      </dl>

      <details className="market-extra">
        <summary>Дополнительно</summary>
        <dl className="value-rows">
          <div>
            <dt>Спред</dt>
            <dd>{formatPercent(price.spreadPct)}</dd>
          </div>
          <div>
            <dt>Long / Short</dt>
            <dd>
              {formatFraction(derivatives.longRatio)} / {formatFraction(derivatives.shortRatio)}
            </dd>
          </div>
          <div>
            <dt>Премия к индексу</dt>
            <dd>{formatPercent(derivatives.premiumVsIndexPct)}</dd>
          </div>
          <div>
            <dt>Дисбаланс стакана 5 / 10 / 20</dt>
            <dd>
              {formatScore(orderBook.imbalanceTop5)} / {formatScore(orderBook.imbalanceTop10)} /{' '}
              {formatScore(orderBook.imbalanceTop20)}
            </dd>
          </div>
          <div>
            <dt>Поток сделок, дельта</dt>
            <dd>{formatSignedPercent(tradeFlow.deltaPct)}</dd>
          </div>
          <div>
            <dt>Режим рынка</dt>
            <dd>{MARKET_REGIME_LABELS[sentiment.marketRegime]}</dd>
          </div>
          {TIMEFRAMES.map(({ key, label }) => (
            <div key={key}>
              <dt>Уровни 2 ({label})</dt>
              <dd>
                {formatPrice(market[key].support2)} / {formatPrice(market[key].resistance2)}
              </dd>
            </div>
          ))}
        </dl>
      </details>
    </section>
  );
}
