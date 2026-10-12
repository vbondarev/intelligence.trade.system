import type { PositionSide, PositionTrackingState } from '../portfolio/portfolioTypes';

export type MarketCategory = 'linear' | 'inverse';

/**
 * Полное текущее состояние позиции. `positionValue` и `unrealizedPnl` выражены в
 * `settlementAsset`, а не в USD; `null` означает «неизвестно», а не ноль.
 */
export interface PositionDetails {
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
  marketCategory: MarketCategory;
  breakEvenPrice: number | null;
  takeProfit: number | null;
  stopLoss: number | null;
  trailingStop: number | null;
  settlementAsset: string;
}

export interface MarketCandle {
  openTimeUtc: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  turnover: number;
}

export type MarketTrend = 'unknown' | 'bullish' | 'bearish' | 'sideways';

export type MarketRegime = 'trending' | 'volatile' | 'meanReversion' | 'neutral';

export interface MarketPrice {
  lastPrice: number;
  markPrice: number;
  indexPrice: number;
  bidPrice: number;
  askPrice: number;
  spreadAbs: number;
  spreadPct: number;
  price24hChangePct: number;
  high24h: number;
  low24h: number;
  volume24h: number;
  turnover24h: number;
}

export interface MarketDerivatives {
  fundingRate: number;
  nextFundingTime: string | null;
  openInterest: number;
  openInterestValue: number;
  openInterestChange1hPct: number;
  openInterestChange4hPct: number;
  longRatio: number;
  shortRatio: number;
  premiumVsIndexPct: number | null;
}

export interface MarketOrderBook {
  capturedAt: string;
  bestBidPrice: number;
  bestAskPrice: number;
  imbalanceTop5: number;
  imbalanceTop10: number;
  imbalanceTop20: number;
}

export interface MarketTradeFlow {
  windowStart: string;
  windowEnd: string;
  buyVolume: number;
  sellVolume: number;
  deltaVolume: number;
  deltaPct: number;
  totalTrades: number;
  hasAggressiveBuyPressure: boolean;
  hasAggressiveSellPressure: boolean;
}

/** Анализ одного timeframe, рассчитанный backend; флаги надёжности передаются как есть. */
export interface MarketTimeframe {
  lastCandle: MarketCandle;
  ema20: number | null;
  ema50: number | null;
  ema200: number | null;
  rsi14: number | null;
  atr14: number | null;
  volumeRatio: number | null;
  trend: MarketTrend;
  trendStrength: number;
  support1: number | null;
  support2: number | null;
  resistance1: number | null;
  resistance2: number | null;
  distanceToSupport1Pct: number | null;
  distanceToResistance1Pct: number | null;
  rsi14IsReliable: boolean;
  emaIsReliable: boolean;
  emaHasFallback: boolean;
  atrIsReliable: boolean;
  atrIsFallback: boolean;
  volumeRatioIsReliable: boolean;
  volumeRatioIsFallback: boolean;
}

export interface MarketSentiment {
  marketRegime: MarketRegime;
  longShortBiasScore: number;
  fundingBiasScore: number;
  orderBookPressureScore: number;
  tradeFlowPressureScore: number;
}

/** Публичный рыночный контекст инструмента позиции на момент `capturedAt`. */
export interface PositionMarket {
  positionId: string;
  exchange: string;
  symbol: string;
  marketCategory: MarketCategory;
  capturedAt: string;
  price: MarketPrice;
  derivatives: MarketDerivatives;
  orderBook: MarketOrderBook;
  tradeFlow: MarketTradeFlow;
  m15: MarketTimeframe;
  h1: MarketTimeframe;
  h4: MarketTimeframe;
  d1: MarketTimeframe;
  sentiment: MarketSentiment;
  tags: string[];
}

/** Интервалы графика страницы позиции; API принимает больше значений, но UI их не предлагает. */
export type ChartInterval = '15m' | '1h' | '4h' | '1d';

export interface PositionCandles {
  positionId: string;
  exchange: string;
  symbol: string;
  marketCategory: MarketCategory;
  interval: string;
  items: MarketCandle[];
}

export type RiskIncreaseDecision = 'allowed' | 'blocked';

export type AssessmentDataQuality = 'freshCompleteReliable' | 'stale' | 'partial' | 'uncertain';

export type AssessmentSafetyState = 'notEvaluated' | 'allowed' | 'blocked';

export type TrendAlignment = 'aligned' | 'adverse' | 'flatOrUnknown';

export type MomentumState = 'normal' | 'overbought' | 'oversold' | 'unavailable';

export type PricePosition = 'below' | 'at' | 'above' | 'unavailable';

export type StopState = 'protective' | 'nonProtective' | 'unknown' | 'unavailable';

export type LiquidationState = 'far' | 'near' | 'invalid' | 'unavailable';

export type PositionAction =
  | 'hold'
  | 'watch'
  | 'protectProfit'
  | 'reduce'
  | 'close'
  | 'moveStop'
  | 'takePartialProfit';

export type AddDecision = 'notEvaluated' | 'doNotAdd' | 'addAllowed';

export type RecommendationPriority = 'low' | 'normal' | 'high' | 'critical';

export type RecommendationStatus = 'active' | 'acknowledged' | 'dismissed' | 'superseded' | 'expired';

export type RecommendationConditionScope = 'action' | 'addDecision' | 'recommendation';

/**
 * Machine-readable reason/condition codes. Набор значений расширяется backend аддитивно,
 * поэтому неизвестный код не делает ответ некорректным и показывается как есть.
 */
export type ReasonCode = string;

export type RecommendationConditionKind = string;

export interface PolicyIdentity {
  version: string;
  hash: string;
}

export interface EvaluationInputIdentity {
  positionId: string;
  exchangeAccountId: string;
  instrumentId: string;
  positionObservedAt: string;
  portfolioCalculatedAt: string;
  marketCapturedAt: string;
}

export interface AssessmentDataQualityState {
  market: AssessmentDataQuality;
  portfolio: AssessmentDataQuality;
  overall: AssessmentDataQuality;
  safetyState: AssessmentSafetyState;
}

export interface AssessmentTrend {
  marketTrend: MarketTrend;
  positionAlignment: TrendAlignment;
  strength: number;
  timeframe: string;
}

export interface AssessmentMomentum {
  rsi14: number | null;
  isReliable: boolean;
  state: MomentumState;
  potentialExhaustion: boolean;
}

export interface AssessmentVolatility {
  atr14: number | null;
  atrPercentOfPrice: number | null;
  isReliable: boolean;
  isFallback: boolean;
}

export interface AssessmentLevels {
  currentPrice: number | null;
  support1: number | null;
  distanceToSupport1Percent: number | null;
  support1Strength: number | null;
  resistance1: number | null;
  distanceToResistance1Percent: number | null;
  resistance1Strength: number | null;
}

export interface AssessmentPnl {
  unrealizedPnl: number | null;
  pnlPercent: number | null;
  averageEntryPrice: number | null;
  currentPrice: number | null;
  priceRelativeToEntry: PricePosition;
  isFavorable: boolean;
}

export interface AssessmentStop {
  stopPrice: number | null;
  distanceFromCurrentPercent: number | null;
  stopRelativeToEntryPercent: number | null;
  state: StopState;
  priceRelativeToEntry: PricePosition;
  trailingStopDistance: number | null;
  hasTrailingStop: boolean;
}

export interface AssessmentBreakeven {
  breakEvenPrice: number | null;
  distanceFromCurrentPercent: number | null;
  distanceFromEntryPercent: number | null;
  priceRelativeToBreakEven: PricePosition;
  isProfitable: boolean;
}

export interface AssessmentLiquidation {
  liquidationPrice: number | null;
  distanceFromCurrentPercent: number | null;
  state: LiquidationState;
}

export interface AssessmentPortfolioRisk {
  policyDecision: RiskIncreaseDecision;
  freeCapitalPercent: number | null;
  grossExposureToEquityPercent: number | null;
  largestPositionConcentrationPercent: number | null;
  totalUnrealizedPnl: number | null;
  usedCapital: number | null;
  isComplete: boolean;
  isFresh: boolean;
  totalEquity: number | null;
  availableCapital: number | null;
  currentPositionValue: number | null;
  currentPositionConcentrationPercent: number | null;
  minimumFreeCapitalPercent: number | null;
  maximumGrossExposureToEquityPercent: number | null;
  maximumPositionConcentrationPercent: number | null;
}

export interface AssessmentResult {
  positionSide: PositionSide;
  currentPrice: number | null;
  trend: AssessmentTrend;
  momentum: AssessmentMomentum;
  volatility: AssessmentVolatility;
  levels: AssessmentLevels;
  pnl: AssessmentPnl;
  stop: AssessmentStop;
  breakeven: AssessmentBreakeven;
  liquidation: AssessmentLiquidation;
  portfolioRisk: AssessmentPortfolioRisk;
}

export interface PositionAssessment {
  id: string;
  evaluatedAt: string;
  validUntil: string;
  ruleVersion: string;
  isLegacy: boolean;
  inputIdentity: EvaluationInputIdentity;
  basePolicyIdentity: PolicyIdentity;
  effectiveConfigurationIdentity: PolicyIdentity;
  portfolioRiskDecision: RiskIncreaseDecision;
  reasonCodes: ReasonCode[];
  dataQuality: AssessmentDataQualityState;
  /** `null`: оценка выполнена, но детерминированный результат не сформирован. */
  result: AssessmentResult | null;
}

export interface RecommendationActionDetails {
  value: PositionAction;
  confidence: number | null;
  priority: RecommendationPriority | null;
  reasonCodes: ReasonCode[];
}

export interface RecommendationAddConditions {
  requiredTrendAlignment: TrendAlignment;
  requiredMomentumState: MomentumState;
  protectiveStopRequired: boolean;
  minimumLiquidationDistancePercent: number;
}

export interface RecommendationAddDecision {
  value: AddDecision;
  reasonCodes: ReasonCode[];
  maximumAdditionalPositionValue: number | null;
  maximumAdditionalQuantity: number | null;
  conditions: RecommendationAddConditions | null;
}

export interface RecommendationCondition {
  scope: RecommendationConditionScope;
  kind: RecommendationConditionKind;
}

export interface RecommendationContinuation {
  nextEvaluationAt: string;
  invalidationConditions: RecommendationCondition[];
  reevaluationConditions: RecommendationCondition[];
}

export interface PositionRecommendation {
  id: string;
  assessmentId: string;
  createdAt: string;
  validUntil: string;
  status: RecommendationStatus;
  policyIdentity: PolicyIdentity;
  action: RecommendationActionDetails;
  addDecision: RecommendationAddDecision;
  reasonCodes: ReasonCode[];
  continuation: RecommendationContinuation | null;
}

export interface PositionEvaluation {
  positionId: string;
  assessment: PositionAssessment;
  /** `null`: рекомендации нет; это не равнозначно `hold`. */
  recommendation: PositionRecommendation | null;
}

export type PositionNotEvaluableReason =
  | 'closedPosition'
  | 'portfolioUnavailable'
  | 'portfolioInconsistent'
  | 'temporalInconsistency';

export type PositionTimelineItemType = 'positionChange' | 'evaluation' | 'recommendation';

export type PositionChangeKind =
  | 'new'
  | 'updated'
  | 'increased'
  | 'reduced'
  | 'closed'
  | 'markedUnknown'
  | 'markedStale'
  | 'recovered';

export type PositionChangeCause =
  | 'initialObservation'
  | 'exchangeObservation'
  | 'missingFromCompleteObservation'
  | 'positionsObservationFailed'
  | 'partialObservation'
  | 'freshnessExpired'
  | 'observationRestored';

/**
 * Сохранённое наблюдение позиции. `positionValue` и `unrealizedPnl` выражены в активе расчёта
 * позиции; `null` означает «неизвестно», а не ноль.
 */
export interface PositionTimelinePositionSnapshot {
  size: number;
  averageEntryPrice: number | null;
  positionValue: number | null;
  leverage: number | null;
  markPrice: number | null;
  breakEvenPrice: number | null;
  liquidationPrice: number | null;
  unrealizedPnl: number | null;
  takeProfit: number | null;
  stopLoss: number | null;
  trailingStop: number | null;
}

/** Наблюдаемое изменение позиции; это не биржевой ордер и не исполнение. */
export interface PositionTimelinePositionChange {
  sequence: number;
  kind: PositionChangeKind;
  cause: PositionChangeCause;
  trackingStateAfter: PositionTrackingState;
  /** `null`: предыдущее состояние не сохранено. */
  before: PositionTimelinePositionSnapshot | null;
  after: PositionTimelinePositionSnapshot;
}

/** Компактная историческая оценка; полного результата оценки timeline не содержит. */
export interface PositionTimelineEvaluation {
  id: string;
  evaluatedAt: string;
  validUntil: string;
  ruleVersion: string;
  isLegacy: boolean;
  dataQuality: AssessmentDataQualityState;
  portfolioRiskDecision: RiskIncreaseDecision;
  reasonCodes: ReasonCode[];
}

/**
 * Историческая рекомендация. `status` — сохранённый статус записи на момент чтения, а не
 * статус в момент создания.
 */
export interface PositionTimelineRecommendation {
  id: string;
  assessmentId: string;
  createdAt: string;
  validUntil: string;
  status: RecommendationStatus;
  action: PositionAction;
  confidence: number | null;
  priority: RecommendationPriority | null;
  addDecision: AddDecision;
  reasonCodes: ReasonCode[];
  isLegacy: boolean;
}

/** Событие истории: заполнен ровно один payload, соответствующий `type`, остальные равны `null`. */
export type PositionTimelineItem =
  | {
      type: 'positionChange';
      occurredAt: string;
      positionChange: PositionTimelinePositionChange;
      evaluation: null;
      recommendation: null;
    }
  | {
      type: 'evaluation';
      occurredAt: string;
      positionChange: null;
      evaluation: PositionTimelineEvaluation;
      recommendation: null;
    }
  | {
      type: 'recommendation';
      occurredAt: string;
      positionChange: null;
      evaluation: null;
      recommendation: PositionTimelineRecommendation;
    };

export interface PositionTimelinePage {
  items: PositionTimelineItem[];
  /** Opaque cursor: передаётся обратно без интерпретации. */
  nextCursor: string | null;
  hasMore: boolean;
}

export interface PositionTimelineQuery {
  /** Непустой набор выбранных типов; все три типа означают отсутствие фильтра. */
  types: readonly PositionTimelineItemType[];
  cursor?: string | null;
}
