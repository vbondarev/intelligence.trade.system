import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { marketFixture } from '../test/positionFixtures';
import { PositionMarketContext } from './PositionMarketContext';
import type { MarketTimeframe } from './positionTypes';

const normalize = (value: string) => value.replace(/\s/g, ' ');

function valueOf(term: string): string {
  return normalize(screen.getByText(term, { selector: 'dt' }).nextElementSibling?.textContent ?? '');
}

function timeframeValueOf(timeframeLabel: string, term: string): string {
  const card = screen.getByRole('heading', { level: 4, name: timeframeLabel }).parentElement;
  if (card === null) {
    throw new Error(`Карточка таймфрейма ${timeframeLabel} не найдена`);
  }

  return normalize(within(card).getByText(term, { selector: 'dt' }).nextElementSibling?.textContent ?? '');
}

function renderWithPrice(price24hChangePct: number) {
  const market = marketFixture();
  render(<PositionMarketContext market={{ ...market, price: { ...market.price, price24hChangePct } }} />);
}

// Backend выставляет trend = unknown ровно тогда, когда хотя бы одна EMA недоступна (emaIsReliable = false).
const EMA_AVAILABLE: Partial<MarketTimeframe> = { ema200: 59000, emaIsReliable: true, emaHasFallback: false };
const EMA_FALLBACK: Partial<MarketTimeframe> = { ema200: 59000, emaIsReliable: true, emaHasFallback: true };
const EMA_UNAVAILABLE: Partial<MarketTimeframe> = { ema200: null, trend: 'unknown', emaIsReliable: false };

function timeframe(overrides: Partial<MarketTimeframe>): MarketTimeframe {
  return { ...marketFixture().h1, ...overrides };
}

describe('PositionMarketContext', () => {
  it.each([
    [0.0234, '+2,34 %'],
    [-0.0156, '−1,56 %'],
    [0, '0,00 %'],
  ])('shows 24h price change fraction %s as %s', (price24hChangePct, expected) => {
    renderWithPrice(price24hChangePct);

    expect(valueOf('Изменение 24ч')).toBe(expected);
  });

  it('keeps metrics already expressed in percent units unscaled', () => {
    const market = marketFixture();
    render(
      <PositionMarketContext
        market={{
          ...market,
          price: { ...market.price, spreadPct: 0.25 },
          derivatives: { ...market.derivatives, premiumVsIndexPct: 0.05 },
        }}
      />,
    );

    expect(valueOf('Спред')).toBe('0,25 %');
    expect(valueOf('Премия к индексу')).toBe('0,05 %');
    expect(valueOf('Изменение OI 1ч / 4ч')).toBe('+0,40 % / −1,10 %');
    expect(valueOf('Поток сделок, дельта')).toBe('+9,09 %');
    expect(valueOf('Funding')).toBe('+0,01 %');
    expect(valueOf('Long / Short')).toBe('52 % / 48 %');
  });

  it.each([
    ['reliable EMA', { trend: 'bullish', ...EMA_AVAILABLE }, 'Восходящий'],
    ['EMA fallback', { trend: 'bullish', ...EMA_FALLBACK }, 'Восходящий (резервный расчёт)'],
    ['unavailable EMA', { ...EMA_UNAVAILABLE, emaHasFallback: false }, 'Не определён (ненадёжно)'],
    ['unavailable EMA with partial fallback', { ...EMA_UNAVAILABLE, emaHasFallback: true }, 'Не определён (ненадёжно)'],
  ] satisfies [string, Partial<MarketTimeframe>, string][])(
    'marks trend quality for %s',
    (_, overrides, expected) => {
      render(<PositionMarketContext market={marketFixture({ h1: timeframe(overrides) })} />);

      expect(timeframeValueOf('1h', 'Тренд')).toBe(expected);
    },
  );

  it('applies EMA quality flags of each timeframe to its own trend', () => {
    render(
      <PositionMarketContext
        market={marketFixture({
          m15: timeframe({ trend: 'sideways', ...EMA_AVAILABLE }),
          h1: timeframe({ trend: 'bullish', ...EMA_FALLBACK }),
          h4: timeframe({ trend: 'bearish', ...EMA_AVAILABLE }),
          d1: timeframe({ ...EMA_UNAVAILABLE, emaHasFallback: false }),
        })}
      />,
    );

    expect(timeframeValueOf('15m', 'Тренд')).toBe('Боковой');
    expect(timeframeValueOf('1h', 'Тренд')).toBe('Восходящий (резервный расчёт)');
    expect(timeframeValueOf('4h', 'Тренд')).toBe('Нисходящий');
    expect(timeframeValueOf('1d', 'Тренд')).toBe('Не определён (ненадёжно)');
  });

  it('keeps RSI, ATR and volume ratio quality flags independent of EMA flags', () => {
    render(
      <PositionMarketContext
        market={marketFixture({
          h1: timeframe({
            trend: 'bullish',
            ...EMA_FALLBACK,
            rsi14: 58.4,
            rsi14IsReliable: false,
            atrIsFallback: true,
            volumeRatioIsReliable: true,
            volumeRatioIsFallback: false,
          }),
          h4: timeframe({
            trend: 'bearish',
            ...EMA_AVAILABLE,
            rsi14: null,
            rsi14IsReliable: false,
            volumeRatioIsFallback: true,
          }),
        })}
      />,
    );

    expect(timeframeValueOf('1h', 'RSI 14')).toBe('58,4 (ненадёжно)');
    expect(timeframeValueOf('1h', 'ATR 14')).toMatch(/^\S+ \(резервный расчёт\)$/);
    expect(timeframeValueOf('1h', 'Отн. объём')).toBe('1,2');
    expect(timeframeValueOf('4h', 'RSI 14')).toBe('—');
    expect(timeframeValueOf('4h', 'ATR 14')).not.toMatch(/\(/);
    expect(timeframeValueOf('4h', 'Отн. объём')).toBe('1,2 (резервный расчёт)');
  });
});
