import type { ExchangeAccountConnectionStatus } from '../connections/connectionTypes';
import type { PositionSide, PositionTrackingState } from './portfolioTypes';

const LOCALE = 'ru-RU';

export const CONNECTION_STATUS_LABELS: Record<ExchangeAccountConnectionStatus, string> = {
  unknown: 'Не проверено',
  connected: 'Подключено',
  unavailable: 'Недоступно',
  disabled: 'Отключено',
};

export const SIDE_LABELS: Record<PositionSide, string> = {
  long: 'LONG',
  short: 'SHORT',
};

export const TRACKING_STATE_LABELS: Record<PositionTrackingState, string> = {
  active: 'Отслеживается',
  unknown: 'Не подтверждена',
  stale: 'Устарела',
  closed: 'Закрыта',
};

/** Отображение отсутствующего значения: `null` от backend означает «неизвестно», а не ноль. */
export const MISSING_VALUE = '—';

const MONEY = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 2 });
const NUMBER = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 8 });
const RELATIVE = new Intl.RelativeTimeFormat('ru', { numeric: 'auto' });

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

function signOf(value: number, explicitPlus: boolean): string {
  if (value < 0) {
    return '−';
  }

  return explicitPlus && value > 0 ? '+' : '';
}

function usd(value: number | null, explicitPlus: boolean): string {
  return value === null ? MISSING_VALUE : `${signOf(value, explicitPlus)}$${MONEY.format(Math.abs(value))}`;
}

function assetAmount(value: number | null, asset: string, explicitPlus: boolean): string {
  return value === null ? MISSING_VALUE : `${signOf(value, explicitPlus)}${MONEY.format(Math.abs(value))} ${asset}`;
}

/** Account-level сумма в USD. */
export function formatUsd(value: number | null): string {
  return usd(value, false);
}

/** Account-level PnL в USD с явным знаком. */
export function formatSignedUsd(value: number | null): string {
  return usd(value, true);
}

/** Сумма в активе расчёта позиции или группы экспозиции; в USD не конвертируется. */
export function formatAssetAmount(value: number | null, asset: string): string {
  return assetAmount(value, asset, false);
}

/** PnL в активе расчёта позиции с явным знаком. */
export function formatSignedAssetAmount(value: number | null, asset: string): string {
  return assetAmount(value, asset, true);
}

export function formatNumber(value: number | null): string {
  return value === null ? MISSING_VALUE : NUMBER.format(value);
}

export function formatLeverage(value: number | null): string {
  return value === null ? MISSING_VALUE : `${NUMBER.format(value)}x`;
}

export function formatTimestamp(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString(LOCALE, { dateStyle: 'medium', timeStyle: 'short' });
}

/** Относительное время от момента `now`, например «2 минуты назад». */
export function formatRelativeTime(value: string, now: number): string {
  const timestamp = new Date(value).getTime();
  if (Number.isNaN(timestamp)) {
    return value;
  }

  const elapsed = Math.max(0, now - timestamp);
  if (elapsed < MINUTE) {
    return 'только что';
  }

  if (elapsed < HOUR) {
    return RELATIVE.format(-Math.floor(elapsed / MINUTE), 'minute');
  }

  if (elapsed < DAY) {
    return RELATIVE.format(-Math.floor(elapsed / HOUR), 'hour');
  }

  return RELATIVE.format(-Math.floor(elapsed / DAY), 'day');
}
