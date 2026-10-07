import type {
  Portfolio,
  PortfolioCapital,
  PortfolioExposure,
  PositionListItem,
  PositionListQuery,
  PositionPage,
  PositionSide,
  PositionTrackingState,
} from './portfolioTypes';

const ACCOUNTS_URL = '/bff/me/exchange-accounts';
const POSITIONS_URL = '/bff/me/positions';

const SIDES: readonly PositionSide[] = ['long', 'short'];
const TRACKING_STATES: readonly PositionTrackingState[] = ['active', 'unknown', 'stale', 'closed'];

/**
 * Ошибка чтения портфеля или позиций. Хранит только machine-readable поля ProblemDetails:
 * UI ветвится по `status` и `code`, а не по `title`/`detail`.
 */
export class PortfolioApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly traceId: string | null;

  constructor(status: number, code: string | null = null, traceId: string | null = null) {
    super(`Portfolio request failed with status ${status}${code ? ` (${code})` : ''}.`);
    this.name = 'PortfolioApiError';
    this.status = status;
    this.code = code;
    this.traceId = traceId;
  }
}

/** Возвращает `null`, если для подключения ещё нет сохранённого портфеля (`204`). */
export async function getPortfolio(accountId: string, signal?: AbortSignal): Promise<Portfolio | null> {
  const response = await get(`${ACCOUNTS_URL}/${encodeURIComponent(accountId)}/portfolio`, signal);
  if (response.status === 204) {
    return null;
  }

  const body: unknown = await response.json();
  if (!isPortfolio(body)) {
    throw new PortfolioApiError(response.status);
  }

  return body;
}

export async function listPositions(query: PositionListQuery, signal?: AbortSignal): Promise<PositionPage> {
  const response = await get(positionsUrl(query), signal);
  const body: unknown = await response.json();
  if (
    !isObject(body) ||
    !Array.isArray(body.items) ||
    !body.items.every(isPositionListItem) ||
    !isNullableString(body.nextCursor) ||
    typeof body.hasMore !== 'boolean'
  ) {
    throw new PortfolioApiError(response.status);
  }

  return { items: body.items, nextCursor: body.nextCursor, hasMore: body.hasMore };
}

/** Строит URL с фиксированным порядком параметров; незаданные фильтры не передаются. */
export function positionsUrl(query: PositionListQuery): string {
  const params = new URLSearchParams();
  params.set('exchangeAccountId', query.exchangeAccountId);
  if (query.trackingState !== undefined) {
    params.set('trackingState', query.trackingState);
  }

  if (query.side !== undefined) {
    params.set('side', query.side);
  }

  if (query.symbol !== undefined && query.symbol.length > 0) {
    params.set('symbol', query.symbol);
  }

  if (query.cursor !== undefined) {
    params.set('cursor', query.cursor);
  }

  return `${POSITIONS_URL}?${params.toString()}`;
}

async function get(url: string, signal?: AbortSignal): Promise<Response> {
  const response = await fetch(url, {
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
    signal,
  });
  if (!response.ok) {
    throw await toError(response);
  }

  return response;
}

async function toError(response: Response): Promise<PortfolioApiError> {
  const contentType = response.headers.get('Content-Type') ?? '';
  if (!/json/i.test(contentType)) {
    return new PortfolioApiError(response.status);
  }

  let body: unknown;
  try {
    body = await response.json();
  } catch {
    return new PortfolioApiError(response.status);
  }

  return isObject(body)
    ? new PortfolioApiError(
        response.status,
        typeof body.code === 'string' ? body.code : null,
        typeof body.traceId === 'string' ? body.traceId : null,
      )
    : new PortfolioApiError(response.status);
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableNumber(value: unknown): value is number | null {
  return value === null || (typeof value === 'number' && Number.isFinite(value));
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isSettlementAsset(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isCapital(value: unknown): value is PortfolioCapital {
  return (
    isObject(value) &&
    isNullableNumber(value.totalEquity) &&
    isNullableNumber(value.availableCapital) &&
    isNullableNumber(value.totalWalletBalance) &&
    isNullableString(value.observedAt)
  );
}

function isExposure(value: unknown): value is PortfolioExposure {
  return (
    isObject(value) &&
    isSettlementAsset(value.settlementAsset) &&
    isNullableNumber(value.grossExposure) &&
    isNullableNumber(value.longExposure) &&
    isNullableNumber(value.shortExposure)
  );
}

function isPortfolio(value: unknown): value is Portfolio {
  return (
    isObject(value) &&
    typeof value.exchangeAccountId === 'string' &&
    typeof value.calculatedAt === 'string' &&
    isCapital(value.capital) &&
    isNullableNumber(value.totalUnrealizedPnl) &&
    typeof value.positionsFullyReconciled === 'boolean' &&
    typeof value.isComplete === 'boolean' &&
    typeof value.isFresh === 'boolean' &&
    typeof value.currentPositionCount === 'number' &&
    Number.isInteger(value.currentPositionCount) &&
    value.currentPositionCount >= 0 &&
    Array.isArray(value.exposures) &&
    value.exposures.every(isExposure)
  );
}

function isPositionListItem(value: unknown): value is PositionListItem {
  return (
    isObject(value) &&
    typeof value.id === 'string' &&
    typeof value.exchangeAccountId === 'string' &&
    typeof value.symbol === 'string' &&
    typeof value.side === 'string' &&
    (SIDES as readonly string[]).includes(value.side) &&
    typeof value.trackingState === 'string' &&
    (TRACKING_STATES as readonly string[]).includes(value.trackingState) &&
    typeof value.size === 'number' &&
    isNullableNumber(value.averageEntryPrice) &&
    isNullableNumber(value.markPrice) &&
    isNullableNumber(value.positionValue) &&
    isNullableNumber(value.unrealizedPnl) &&
    isNullableNumber(value.leverage) &&
    isNullableNumber(value.liquidationPrice) &&
    typeof value.firstDetectedAt === 'string' &&
    typeof value.lastObservedAt === 'string' &&
    isNullableString(value.closedAt) &&
    isSettlementAsset(value.settlementAsset)
  );
}
