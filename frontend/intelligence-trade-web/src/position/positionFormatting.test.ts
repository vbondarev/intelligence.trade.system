import { describe, expect, it } from 'vitest';
import {
  ACTION_LABELS,
  conditionKindLabel,
  formatCompact,
  formatFraction,
  formatFundingRate,
  formatPercent,
  formatPrice,
  formatScore,
  formatSignedPercent,
  isLaterThan,
  isPast,
  parseChartInterval,
  reasonCodeLabel,
} from './positionFormatting';

const normalize = (value: string) => value.replace(/\s/g, ' ');

describe('positionFormatting', () => {
  it('shows missing values as a dash instead of zero', () => {
    expect(formatPrice(null)).toBe('—');
    expect(formatPercent(null)).toBe('—');
    expect(formatSignedPercent(null)).toBe('—');
    expect(formatFraction(null)).toBe('—');
    expect(formatFundingRate(null)).toBe('—');
    expect(formatScore(null)).toBe('—');
    expect(formatCompact(null)).toBe('—');
  });

  it('keeps zero as a real value', () => {
    expect(formatPrice(0)).toBe('0');
    expect(normalize(formatSignedPercent(0))).toBe('0,00 %');
  });

  it('formats prices without rounding significant decimals', () => {
    expect(normalize(formatPrice(61234.5))).toBe('61 234,5');
    expect(formatPrice(0.00001234)).toBe('0,00001234');
  });

  it('formats percents already expressed in percent units with an explicit sign', () => {
    expect(normalize(formatSignedPercent(-1.25))).toBe('−1,25 %');
    expect(normalize(formatSignedPercent(2.5))).toBe('+2,50 %');
    expect(normalize(formatPercent(-3.333))).toBe('−3,33 %');
  });

  it('converts fractions and funding rate into percent for display only', () => {
    expect(normalize(formatFraction(0.7))).toBe('70 %');
    expect(normalize(formatFundingRate(0.0001))).toBe('+0,01 %');
    expect(normalize(formatFundingRate(-0.000125))).toBe('−0,0125 %');
  });

  it('labels every recommendation action', () => {
    expect(Object.keys(ACTION_LABELS)).toEqual([
      'hold',
      'watch',
      'protectProfit',
      'reduce',
      'close',
      'moveStop',
      'takePartialProfit',
    ]);
  });

  it('labels known codes and shows unknown codes as is', () => {
    expect(reasonCodeLabel('stopMissing')).toBe('Стоп не установлен');
    expect(reasonCodeLabel('futureReasonCode')).toBe('futureReasonCode');
    expect(conditionKindLabel('trendAlignment')).toBe('Соответствие тренду');
    expect(conditionKindLabel('futureKind')).toBe('futureKind');
  });

  it.each([
    ['15m', '15m'],
    ['1h', '1h'],
    ['4h', '4h'],
    ['1d', '1d'],
    [null, '1h'],
    ['', '1h'],
    ['1w', '1h'],
    ['1H', '1h'],
    ['1h&userId=other', '1h'],
  ])('normalizes chart interval %s to %s', (value, expected) => {
    expect(parseChartInterval(value)).toBe(expected);
  });

  it('compares timestamps without guessing invalid values', () => {
    const now = Date.parse('2026-10-12T10:00:00Z');
    expect(isPast('2026-10-12T09:59:59Z', now)).toBe(true);
    expect(isPast('2026-10-12T10:00:01Z', now)).toBe(false);
    expect(isPast('not a date', now)).toBe(false);
    expect(isLaterThan('2026-10-12T09:05:00Z', '2026-10-12T09:00:30Z')).toBe(true);
    expect(isLaterThan('2026-10-12T09:00:30Z', '2026-10-12T09:00:30Z')).toBe(false);
    expect(isLaterThan('not a date', '2026-10-12T09:00:30Z')).toBe(false);
  });
});
