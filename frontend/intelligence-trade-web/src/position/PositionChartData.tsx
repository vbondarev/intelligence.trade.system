import { formatChartPrice } from './chartPriceFormat';
import type { ChartCandle, PriceLevel, PriceLevelKind } from './positionChartAdapter';
import type { ChartInterval } from './positionTypes';

const LEVEL_LABELS: Record<PriceLevelKind, string> = {
  entry: 'Цена входа',
  breakEven: 'Безубыток',
  stopLoss: 'Stop Loss',
  takeProfit: 'Take Profit',
  liquidation: 'Ликвидация',
  support: 'Поддержка',
  resistance: 'Сопротивление',
};

interface PositionChartDataProps {
  symbol: string;
  interval: ChartInterval;
  candles: readonly ChartCandle[];
  levels: readonly PriceLevel[];
  loading: boolean;
  dataUnavailable: boolean;
  showingPreviousData: boolean;
  levelsIncomplete: boolean;
}

export function PositionChartData({
  symbol,
  interval,
  candles,
  levels,
  loading,
  dataUnavailable,
  showingPreviousData,
  levelsIncomplete,
}: PositionChartDataProps) {
  return (
    <details className="chart-data">
      <summary>Данные графика и ценовые уровни</summary>
      <p>{symbol} · интервал {interval} · время UTC</p>
      {loading && <p>Свечи загружаются.</p>}
      {dataUnavailable && <p>Данные свечей недоступны.</p>}
      {showingPreviousData && <p>Показаны ранее полученные свечи, их актуальность не подтверждена.</p>}
      {!loading && !dataUnavailable && !showingPreviousData && candles.length === 0 && (
        <p>Свечей для выбранного интервала нет.</p>
      )}
      {candles.length > 0 && (
        <div className="chart-data-table" role="region" aria-label="Таблица свечей" tabIndex={0}>
          <table>
            <caption>Свечи {symbol}, {interval}, UTC</caption>
            <thead>
              <tr><th scope="col">Время UTC</th><th scope="col">Open</th><th scope="col">High</th><th scope="col">Low</th><th scope="col">Close</th></tr>
            </thead>
            <tbody>
              {candles.map((candle) => {
                const timestamp = new Date(candle.time * 1000).toISOString();
                return (
                  <tr key={candle.time}>
                    <th scope="row"><time dateTime={timestamp}>{timestamp.replace('T', ' ').replace('.000Z', ' UTC')}</time></th>
                    <td>{formatChartPrice(candle.open)}</td>
                    <td>{formatChartPrice(candle.high)}</td>
                    <td>{formatChartPrice(candle.low)}</td>
                    <td>{formatChartPrice(candle.close)}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {levelsIncomplete && (
        <p>Данные позиции или рынка загружаются либо недоступны. Актуальность и полнота уровней не подтверждены.</p>
      )}
      {levels.length > 0 ? (
        <div className="chart-data-table" role="region" aria-label="Таблица ценовых уровней" tabIndex={0}>
          <table>
            <caption>Ценовые уровни {symbol}</caption>
            <thead><tr><th scope="col">Уровень</th><th scope="col">Цена</th></tr></thead>
            <tbody>
              {levels.map((level) => (
                <tr key={level.kind}><th scope="row">{LEVEL_LABELS[level.kind]}</th><td>{formatChartPrice(level.price)}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : !levelsIncomplete && <p>Ценовые уровни отсутствуют.</p>}
    </details>
  );
}
