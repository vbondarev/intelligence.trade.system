export type PositionSide = 'long' | 'short';

export type PositionTrackingState = 'active' | 'unknown' | 'stale' | 'closed';

/** Account-level капитал: значения нормализованы биржей в USD. */
export interface PortfolioCapital {
  totalEquity: number | null;
  availableCapital: number | null;
  totalWalletBalance: number | null;
  observedAt: string | null;
}

/**
 * Экспозиция одного актива расчёта. Значения разных активов не складываются и не
 * конвертируются между собой.
 */
export interface PortfolioExposure {
  settlementAsset: string;
  grossExposure: number | null;
  longExposure: number | null;
  shortExposure: number | null;
}

export interface Portfolio {
  exchangeAccountId: string;
  calculatedAt: string;
  capital: PortfolioCapital;
  /** Account-level нереализованный PnL в USD, а не сумма PnL позиций. */
  totalUnrealizedPnl: number | null;
  positionsFullyReconciled: boolean;
  isComplete: boolean;
  isFresh: boolean;
  currentPositionCount: number;
  exposures: PortfolioExposure[];
}

/** Позиция списка. `positionValue` и `unrealizedPnl` выражены в `settlementAsset`, а не в USD. */
export interface PositionListItem {
  id: string;
  exchangeAccountId: string;
  symbol: string;
  side: PositionSide;
  trackingState: PositionTrackingState;
  size: number;
  averageEntryPrice: number | null;
  markPrice: number | null;
  positionValue: number | null;
  unrealizedPnl: number | null;
  leverage: number | null;
  liquidationPrice: number | null;
  firstDetectedAt: string;
  lastObservedAt: string;
  closedAt: string | null;
  settlementAsset: string;
}

/** Страница cursor pagination: `nextCursor` — opaque значение, клиент его не разбирает. */
export interface PositionPage {
  items: PositionListItem[];
  nextCursor: string | null;
  hasMore: boolean;
}

/** Режим списка: текущие позиции (без фильтра — active, unknown и stale) или закрытые. */
export type PositionsView = 'current' | 'closed';

/** Фильтр состояния, доступный только для текущих позиций. */
export type CurrentTrackingState = Exclude<PositionTrackingState, 'closed'>;

export interface PositionFilters {
  view: PositionsView;
  state: CurrentTrackingState | null;
  side: PositionSide | null;
  symbol: string | null;
}

export interface PositionListQuery {
  exchangeAccountId: string;
  trackingState?: PositionTrackingState;
  side?: PositionSide;
  symbol?: string;
  cursor?: string;
}
