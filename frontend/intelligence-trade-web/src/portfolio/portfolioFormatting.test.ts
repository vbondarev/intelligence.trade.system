import { describe, expect, it } from 'vitest';
import {
  MISSING_VALUE,
  formatAssetAmount,
  formatLeverage,
  formatNumber,
  formatRelativeTime,
  formatSignedAssetAmount,
  formatSignedUsd,
  formatUsd,
} from './portfolioFormatting';

// ru-RU разделяет разряды неразрывным пробелом; тесты сравнивают текст с обычными пробелами.
const plain = (value: string) => value.replace(/\s/g, ' ');

describe('portfolioFormatting', () => {
  it('formats account-level USD values', () => {
    expect(plain(formatUsd(18420))).toBe('$18 420');
    expect(plain(formatUsd(7310.5))).toBe('$7 310,5');
    expect(plain(formatUsd(-15))).toBe('−$15');
    expect(formatUsd(null)).toBe(MISSING_VALUE);
  });

  it('formats signed USD PnL', () => {
    expect(formatSignedUsd(265)).toBe('+$265');
    expect(formatSignedUsd(-15.25)).toBe('−$15,25');
    expect(formatSignedUsd(0)).toBe('$0');
    expect(formatSignedUsd(null)).toBe(MISSING_VALUE);
  });

  it('formats amounts in the settlement asset without USD conversion', () => {
    expect(plain(formatAssetAmount(13000, 'USDT'))).toBe('13 000 USDT');
    expect(formatAssetAmount(0, 'USDC')).toBe('0 USDC');
    expect(formatAssetAmount(null, 'USDT')).toBe(MISSING_VALUE);
    expect(formatSignedAssetAmount(24.5, 'USDT')).toBe('+24,5 USDT');
    expect(formatSignedAssetAmount(-3, 'USDC')).toBe('−3 USDC');
  });

  it('formats quantities, prices and leverage', () => {
    expect(formatNumber(0.015)).toBe('0,015');
    expect(plain(formatNumber(68500))).toBe('68 500');
    expect(formatNumber(null)).toBe(MISSING_VALUE);
    expect(formatLeverage(5)).toBe('5x');
    expect(formatLeverage(null)).toBe(MISSING_VALUE);
  });

  it('formats relative time from the given moment', () => {
    const now = Date.parse('2026-10-07T10:00:00Z');
    expect(formatRelativeTime('2026-10-07T09:59:30Z', now)).toBe('только что');
    expect(formatRelativeTime('2026-10-07T09:58:00Z', now)).toBe('2 минуты назад');
    expect(formatRelativeTime('2026-10-07T07:00:00Z', now)).toBe('3 часа назад');
    expect(formatRelativeTime('2026-10-05T10:00:00Z', now)).toBe('позавчера');
    expect(formatRelativeTime('not-a-date', now)).toBe('not-a-date');
  });
});
