import type { CandlestickData, UTCTimestamp } from 'lightweight-charts';
import type { ChartInterval, MarketCandle, MarketTimeframe, PositionDetails, PositionMarket } from './positionTypes';

export type ChartCandle = CandlestickData<UTCTimestamp>;

export type PriceLevelKind = 'entry' | 'breakEven' | 'stopLoss' | 'takeProfit' | 'liquidation' | 'support' | 'resistance';

export interface PriceLevel {
  kind: PriceLevelKind;
  title: string;
  price: number;
  color: string;
  dashed: boolean;
}

const LEVEL_STYLES: Record<PriceLevelKind, { title: string; color: string; dashed: boolean }> = {
  entry: { title: 'Entry', color: '#60a5fa', dashed: false },
  breakEven: { title: 'BreakEven', color: '#c084fc', dashed: true },
  stopLoss: { title: 'SL', color: '#f87171', dashed: false },
  takeProfit: { title: 'TP', color: '#4ade80', dashed: false },
  liquidation: { title: 'Liquidation', color: '#fb923c', dashed: false },
  support: { title: 'Support', color: '#22d3ee', dashed: true },
  resistance: { title: 'Resistance', color: '#facc15', dashed: true },
};

const TIMEFRAME_BY_INTERVAL: Record<ChartInterval, 'm15' | 'h1' | 'h4' | 'd1'> = {
  '15m': 'm15',
  '1h': 'h1',
  '4h': 'h4',
  '1d': 'd1',
};

/**
 * Преобразует свечи API в формат библиотеки: время открытия — UTC seconds, порядок строго
 * возрастающий. Свечи с некорректным временем или нечисловыми OHLC отбрасываются; при
 * повторе времени сохраняется последняя полученная свеча, потому что библиотека требует
 * уникальные timestamps.
 */
export function toChartCandles(items: readonly MarketCandle[]): ChartCandle[] {
  const byTime = new Map<number, ChartCandle>();
  for (const item of items) {
    const milliseconds = Date.parse(item.openTimeUtc);
    if (
      Number.isNaN(milliseconds) ||
      !Number.isFinite(item.open) ||
      !Number.isFinite(item.high) ||
      !Number.isFinite(item.low) ||
      !Number.isFinite(item.close)
    ) {
      continue;
    }

    const time = Math.floor(milliseconds / 1000) as UTCTimestamp;
    byTime.set(time, { time, open: item.open, high: item.high, low: item.low, close: item.close });
  }

  return [...byTime.values()].sort((left, right) => left.time - right.time);
}

/**
 * Уровни позиции и ближайшие support/resistance выбранного timeframe. Отсутствующее значение
 * не изображается; собственных уровней frontend не вычисляет.
 */
export function toPriceLevels(
  position: PositionDetails | null,
  market: PositionMarket | null,
  interval: ChartInterval,
): PriceLevel[] {
  const timeframe: MarketTimeframe | null = market === null ? null : market[TIMEFRAME_BY_INTERVAL[interval]];
  const values: [PriceLevelKind, number | null | undefined][] = [
    ['entry', position?.averageEntryPrice],
    ['breakEven', position?.breakEvenPrice],
    ['stopLoss', position?.stopLoss],
    ['takeProfit', position?.takeProfit],
    ['liquidation', position?.liquidationPrice],
    ['support', timeframe?.support1],
    ['resistance', timeframe?.resistance1],
  ];

  return values.flatMap(([kind, price]) =>
    typeof price === 'number' && Number.isFinite(price) && price > 0 ? [{ kind, price, ...LEVEL_STYLES[kind] }] : [],
  );
}
