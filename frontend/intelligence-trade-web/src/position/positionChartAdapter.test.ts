import { describe, expect, it } from 'vitest';
import { candleFixture, marketFixture, positionFixture } from '../test/positionFixtures';
import { toChartCandles, toPriceLevels } from './positionChartAdapter';

describe('toChartCandles', () => {
  it('converts OHLC to UTC seconds keeping values unchanged', () => {
    const candle = candleFixture('2026-10-12T08:00:00Z', 61000);

    expect(toChartCandles([candle])).toEqual([
      { time: Date.UTC(2026, 9, 12, 8) / 1000, open: 60900, high: 61200, low: 60700, close: 61000 },
    ]);
  });

  it('interprets offsets as UTC instants', () => {
    const [candle] = toChartCandles([candleFixture('2026-10-12T11:00:00+03:00')]);

    expect(candle?.time).toBe(Date.UTC(2026, 9, 12, 8) / 1000);
  });

  it('sorts ascending and keeps the last candle for a repeated time', () => {
    const result = toChartCandles([
      candleFixture('2026-10-12T08:00:00Z', 100),
      candleFixture('2026-10-12T06:00:00Z', 200),
      candleFixture('2026-10-12T08:00:00Z', 300),
      candleFixture('2026-10-12T07:00:00Z', 400),
    ]);

    expect(result.map((candle) => candle.close)).toEqual([200, 400, 300]);
    expect(result.map((candle) => candle.time)).toEqual(
      [6, 7, 8].map((hour) => Date.UTC(2026, 9, 12, hour) / 1000),
    );
  });

  it('drops candles with an invalid time or non-finite prices', () => {
    const valid = candleFixture('2026-10-12T08:00:00Z');

    const result = toChartCandles([
      { ...valid, openTimeUtc: 'not-a-date' },
      { ...candleFixture('2026-10-12T05:00:00Z'), high: Number.NaN },
      { ...candleFixture('2026-10-12T06:00:00Z'), close: Number.POSITIVE_INFINITY },
      valid,
    ]);

    expect(result).toHaveLength(1);
    expect(result[0]?.close).toBe(valid.close);
  });

  it('returns an empty series for no candles', () => {
    expect(toChartCandles([])).toEqual([]);
  });
});

describe('toPriceLevels', () => {
  it('builds position levels and support/resistance of the timeframe matching the interval', () => {
    const market = marketFixture({
      m15: { ...marketFixture().m15, support1: 60500, resistance1: 61500 },
      h4: { ...marketFixture().h4, support1: 57000, resistance1: 66000 },
    });

    const levels = toPriceLevels(positionFixture(), market, '4h');

    expect(levels.map((level) => [level.kind, level.title, level.price])).toEqual([
      ['entry', 'Entry', 60000],
      ['breakEven', 'BreakEven', 60100],
      ['stopLoss', 'SL', 58000],
      ['takeProfit', 'TP', 65000],
      ['liquidation', 'Liquidation', 50000],
      ['support', 'Support', 57000],
      ['resistance', 'Resistance', 66000],
    ]);
    expect(toPriceLevels(positionFixture(), market, '15m').find((level) => level.kind === 'support')?.price).toBe(60500);
  });

  it('omits missing, zero and negative values', () => {
    const position = positionFixture({
      breakEvenPrice: null,
      takeProfit: 0,
      stopLoss: -1,
      liquidationPrice: null,
    });
    const market = marketFixture({ h1: { ...marketFixture().h1, support1: null, resistance1: Number.NaN } });

    expect(toPriceLevels(position, market, '1h').map((level) => level.kind)).toEqual(['entry']);
  });

  it('works while position or market are not loaded', () => {
    expect(toPriceLevels(null, null, '1h')).toEqual([]);
    expect(toPriceLevels(null, marketFixture(), '1d').map((level) => level.kind)).toEqual(['support', 'resistance']);
    expect(toPriceLevels(positionFixture(), null, '1h').map((level) => level.kind)).not.toContain('support');
  });

  it('uses distinct colors for every level kind', () => {
    const levels = toPriceLevels(positionFixture(), marketFixture(), '1h');

    expect(new Set(levels.map((level) => level.color)).size).toBe(levels.length);
  });
});
