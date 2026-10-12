import type { PositionSide, PositionTrackingState } from '../portfolio/portfolioTypes';
import type {
  AddDecision,
  AssessmentBreakeven,
  AssessmentDataQuality,
  AssessmentDataQualityState,
  AssessmentLevels,
  AssessmentLiquidation,
  AssessmentMomentum,
  AssessmentPnl,
  AssessmentPortfolioRisk,
  AssessmentResult,
  AssessmentSafetyState,
  AssessmentStop,
  AssessmentTrend,
  AssessmentVolatility,
  ChartInterval,
  EvaluationInputIdentity,
  LiquidationState,
  MarketCandle,
  MarketCategory,
  MarketDerivatives,
  MarketOrderBook,
  MarketPrice,
  MarketRegime,
  MarketSentiment,
  MarketTimeframe,
  MarketTradeFlow,
  MarketTrend,
  MomentumState,
  PolicyIdentity,
  PositionAction,
  PositionAssessment,
  PositionCandles,
  PositionChangeCause,
  PositionChangeKind,
  PositionDetails,
  PositionEvaluation,
  PositionMarket,
  PositionNotEvaluableReason,
  PositionRecommendation,
  PositionTimelineEvaluation,
  PositionTimelineItem,
  PositionTimelineItemType,
  PositionTimelinePage,
  PositionTimelinePositionChange,
  PositionTimelinePositionSnapshot,
  PositionTimelineQuery,
  PositionTimelineRecommendation,
  PricePosition,
  RecommendationActionDetails,
  RecommendationAddConditions,
  RecommendationAddDecision,
  RecommendationCondition,
  RecommendationConditionScope,
  RecommendationContinuation,
  RecommendationPriority,
  RecommendationStatus,
  RiskIncreaseDecision,
  StopState,
  TrendAlignment,
} from './positionTypes';

const POSITIONS_URL = '/bff/me/positions';
const ANTIFORGERY_URL = '/bff/auth/antiforgery';
const CSRF_HEADER = 'X-CSRF-TOKEN';

/** Начальный и единственный размер выборки свечей страницы позиции. */
export const CANDLES_LIMIT = 200;

/** Размер страницы истории в Web; default API (50) не меняется. */
export const TIMELINE_PAGE_SIZE = 25;

/** Поддерживаемые типы событий истории в фиксированном порядке передачи `type`. */
export const TIMELINE_ITEM_TYPES: readonly PositionTimelineItemType[] = ['positionChange', 'evaluation', 'recommendation'];

const SIDES: readonly PositionSide[] = ['long', 'short'];
const TRACKING_STATES: readonly PositionTrackingState[] = ['active', 'unknown', 'stale', 'closed'];
const MARKET_CATEGORIES: readonly MarketCategory[] = ['linear', 'inverse'];
const MARKET_TRENDS: readonly MarketTrend[] = ['unknown', 'bullish', 'bearish', 'sideways'];
const MARKET_REGIMES: readonly MarketRegime[] = ['trending', 'volatile', 'meanReversion', 'neutral'];
const RISK_DECISIONS: readonly RiskIncreaseDecision[] = ['allowed', 'blocked'];
const DATA_QUALITIES: readonly AssessmentDataQuality[] = ['freshCompleteReliable', 'stale', 'partial', 'uncertain'];
const SAFETY_STATES: readonly AssessmentSafetyState[] = ['notEvaluated', 'allowed', 'blocked'];
const TREND_ALIGNMENTS: readonly TrendAlignment[] = ['aligned', 'adverse', 'flatOrUnknown'];
const MOMENTUM_STATES: readonly MomentumState[] = ['normal', 'overbought', 'oversold', 'unavailable'];
const PRICE_POSITIONS: readonly PricePosition[] = ['below', 'at', 'above', 'unavailable'];
const STOP_STATES: readonly StopState[] = ['protective', 'nonProtective', 'unknown', 'unavailable'];
const LIQUIDATION_STATES: readonly LiquidationState[] = ['far', 'near', 'invalid', 'unavailable'];
const ACTIONS: readonly PositionAction[] = [
  'hold',
  'watch',
  'protectProfit',
  'reduce',
  'close',
  'moveStop',
  'takePartialProfit',
];
const ADD_DECISIONS: readonly AddDecision[] = ['notEvaluated', 'doNotAdd', 'addAllowed'];
const PRIORITIES: readonly RecommendationPriority[] = ['low', 'normal', 'high', 'critical'];
const STATUSES: readonly RecommendationStatus[] = ['active', 'acknowledged', 'dismissed', 'superseded', 'expired'];
const CONDITION_SCOPES: readonly RecommendationConditionScope[] = ['action', 'addDecision', 'recommendation'];
const NOT_EVALUABLE_REASONS: readonly PositionNotEvaluableReason[] = [
  'closedPosition',
  'portfolioUnavailable',
  'portfolioInconsistent',
  'temporalInconsistency',
];
const CHANGE_KINDS: readonly PositionChangeKind[] = [
  'new',
  'updated',
  'increased',
  'reduced',
  'closed',
  'markedUnknown',
  'markedStale',
  'recovered',
];
const CHANGE_CAUSES: readonly PositionChangeCause[] = [
  'initialObservation',
  'exchangeObservation',
  'missingFromCompleteObservation',
  'positionsObservationFailed',
  'partialObservation',
  'freshnessExpired',
  'observationRestored',
];

/**
 * Ошибка операций страницы позиции. Хранит только machine-readable поля ProblemDetails:
 * UI ветвится по `status`, `code` и `reason`, а не по `title`/`detail`.
 */
export class PositionApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly traceId: string | null;
  /** Причина `409 position_not_evaluable`; неизвестное значение сохраняется как `null`. */
  readonly reason: PositionNotEvaluableReason | null;

  constructor(
    status: number,
    code: string | null = null,
    traceId: string | null = null,
    reason: PositionNotEvaluableReason | null = null,
  ) {
    super(`Запрос позиции завершился со статусом ${status}${code ? ` (${code})` : ''}.`);
    this.name = 'PositionApiError';
    this.status = status;
    this.code = code;
    this.traceId = traceId;
    this.reason = reason;
  }
}

export async function getPosition(positionId: string, signal?: AbortSignal): Promise<PositionDetails> {
  const response = await get(positionUrl(positionId), signal);
  return readBody(response, isPositionDetails);
}

export async function getPositionMarket(positionId: string, signal?: AbortSignal): Promise<PositionMarket> {
  const response = await get(`${positionUrl(positionId)}/market`, signal);
  return readBody(response, isPositionMarket);
}

export async function getPositionCandles(
  positionId: string,
  interval: ChartInterval,
  signal?: AbortSignal,
): Promise<PositionCandles> {
  const response = await get(candlesUrl(positionId, interval), signal);
  return readBody(response, isPositionCandles);
}

/** Возвращает `null`, если оценка позиции ещё не выполнялась (`204`). */
export async function getPositionEvaluation(
  positionId: string,
  signal?: AbortSignal,
): Promise<PositionEvaluation | null> {
  const response = await get(`${positionUrl(positionId)}/evaluation`, signal);
  if (response.status === 204) {
    return null;
  }

  return readBody(response, isPositionEvaluation);
}

/**
 * Явная оценка позиции: один `POST` без body с antiforgery token. Автоматических повторов нет:
 * решение о новой попытке принимает пользователь.
 */
export async function evaluatePosition(positionId: string): Promise<PositionEvaluation> {
  const requestToken = await fetchAntiforgeryToken();
  const response = await fetch(`${positionUrl(positionId)}/evaluation`, {
    method: 'POST',
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json', [CSRF_HEADER]: requestToken },
  });
  if (!response.ok) {
    throw await toError(response);
  }

  return readBody(response, isPositionEvaluation);
}

/**
 * Одна страница истории позиции. Порядок событий, фильтрация и cursor определяются API;
 * автоматических повторов нет.
 */
export async function getPositionTimeline(
  positionId: string,
  query: PositionTimelineQuery,
  signal?: AbortSignal,
): Promise<PositionTimelinePage> {
  const response = await get(timelineUrl(positionId, query), signal);
  return readBody(response, isTimelinePage);
}

export function positionUrl(positionId: string): string {
  return `${POSITIONS_URL}/${encodeURIComponent(positionId)}`;
}

export function candlesUrl(positionId: string, interval: ChartInterval): string {
  const params = new URLSearchParams({ interval, limit: String(CANDLES_LIMIT) });
  return `${positionUrl(positionId)}/candles?${params.toString()}`;
}

/**
 * URL страницы истории. Выбор всех типов передаётся отсутствием `type` (default API);
 * пустой набор запрещён, потому что API трактует отсутствие `type` как «все типы».
 */
export function timelineUrl(positionId: string, query: PositionTimelineQuery): string {
  const selected = TIMELINE_ITEM_TYPES.filter((type) => query.types.includes(type));
  if (selected.length === 0) {
    throw new RangeError('Для запроса истории позиции необходимо выбрать хотя бы один тип событий.');
  }

  const params = new URLSearchParams({ pageSize: String(TIMELINE_PAGE_SIZE) });
  if (selected.length < TIMELINE_ITEM_TYPES.length) {
    for (const type of selected) {
      params.append('type', type);
    }
  }

  if (query.cursor) {
    params.append('cursor', query.cursor);
  }

  return `${positionUrl(positionId)}/timeline?${params.toString()}`;
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

async function fetchAntiforgeryToken(): Promise<string> {
  const response = await fetch(ANTIFORGERY_URL, {
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new PositionApiError(response.status);
  }

  const body = await readJson(response);
  if (!isObject(body) || typeof body.requestToken !== 'string' || body.requestToken.length === 0) {
    throw new PositionApiError(response.status);
  }

  return body.requestToken;
}

async function readBody<T>(response: Response, guard: (value: unknown) => value is T): Promise<T> {
  const body = await readJson(response);
  if (!guard(body)) {
    throw new PositionApiError(response.status);
  }

  return body;
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return (await response.json()) as unknown;
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }

    throw new PositionApiError(response.status);
  }
}

async function toError(response: Response): Promise<PositionApiError> {
  const contentType = response.headers.get('Content-Type') ?? '';
  if (!/json/i.test(contentType)) {
    return new PositionApiError(response.status);
  }

  let body: unknown;
  try {
    body = await response.json();
  } catch {
    return new PositionApiError(response.status);
  }

  if (!isObject(body)) {
    return new PositionApiError(response.status);
  }

  const code = typeof body.code === 'string' ? body.code : null;
  return new PositionApiError(
    response.status,
    code,
    typeof body.traceId === 'string' ? body.traceId : null,
    response.status === 409 && code === 'position_not_evaluable' && isOneOf(body.reason, NOT_EVALUABLE_REASONS)
      ? body.reason
      : null,
  );
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNumber(value: unknown): value is number {
  return typeof value === 'number' && Number.isFinite(value);
}

function isNullableNumber(value: unknown): value is number | null {
  return value === null || isNumber(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isOneOf<T extends string>(value: unknown, values: readonly T[]): value is T {
  return typeof value === 'string' && (values as readonly string[]).includes(value);
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string');
}

function isPositionDetails(value: unknown): value is PositionDetails {
  return (
    isObject(value) &&
    typeof value.id === 'string' &&
    typeof value.exchangeAccountId === 'string' &&
    isNonEmptyString(value.symbol) &&
    isOneOf(value.side, SIDES) &&
    isOneOf(value.trackingState, TRACKING_STATES) &&
    isNumber(value.size) &&
    isNullableNumber(value.averageEntryPrice) &&
    isNullableNumber(value.markPrice) &&
    isNullableNumber(value.positionValue) &&
    isNullableNumber(value.unrealizedPnl) &&
    isNullableNumber(value.leverage) &&
    isNullableNumber(value.liquidationPrice) &&
    typeof value.firstDetectedAt === 'string' &&
    typeof value.lastObservedAt === 'string' &&
    isNullableString(value.closedAt) &&
    isOneOf(value.marketCategory, MARKET_CATEGORIES) &&
    isNullableNumber(value.breakEvenPrice) &&
    isNullableNumber(value.takeProfit) &&
    isNullableNumber(value.stopLoss) &&
    isNullableNumber(value.trailingStop) &&
    isNonEmptyString(value.settlementAsset)
  );
}

function isCandle(value: unknown): value is MarketCandle {
  return (
    isObject(value) &&
    typeof value.openTimeUtc === 'string' &&
    isNumber(value.open) &&
    isNumber(value.high) &&
    isNumber(value.low) &&
    isNumber(value.close) &&
    isNumber(value.volume) &&
    isNumber(value.turnover)
  );
}

function isPrice(value: unknown): value is MarketPrice {
  return (
    isObject(value) &&
    isNumber(value.lastPrice) &&
    isNumber(value.markPrice) &&
    isNumber(value.indexPrice) &&
    isNumber(value.bidPrice) &&
    isNumber(value.askPrice) &&
    isNumber(value.spreadAbs) &&
    isNumber(value.spreadPct) &&
    isNumber(value.price24hChangePct) &&
    isNumber(value.high24h) &&
    isNumber(value.low24h) &&
    isNumber(value.volume24h) &&
    isNumber(value.turnover24h)
  );
}

function isDerivatives(value: unknown): value is MarketDerivatives {
  return (
    isObject(value) &&
    isNumber(value.fundingRate) &&
    isNullableString(value.nextFundingTime) &&
    isNumber(value.openInterest) &&
    isNumber(value.openInterestValue) &&
    isNumber(value.openInterestChange1hPct) &&
    isNumber(value.openInterestChange4hPct) &&
    isNumber(value.longRatio) &&
    isNumber(value.shortRatio) &&
    isNullableNumber(value.premiumVsIndexPct)
  );
}

function isOrderBook(value: unknown): value is MarketOrderBook {
  return (
    isObject(value) &&
    typeof value.capturedAt === 'string' &&
    isNumber(value.bestBidPrice) &&
    isNumber(value.bestAskPrice) &&
    isNumber(value.imbalanceTop5) &&
    isNumber(value.imbalanceTop10) &&
    isNumber(value.imbalanceTop20)
  );
}

function isTradeFlow(value: unknown): value is MarketTradeFlow {
  return (
    isObject(value) &&
    typeof value.windowStart === 'string' &&
    typeof value.windowEnd === 'string' &&
    isNumber(value.buyVolume) &&
    isNumber(value.sellVolume) &&
    isNumber(value.deltaVolume) &&
    isNumber(value.deltaPct) &&
    Number.isInteger(value.totalTrades) &&
    typeof value.hasAggressiveBuyPressure === 'boolean' &&
    typeof value.hasAggressiveSellPressure === 'boolean'
  );
}

function isTimeframe(value: unknown): value is MarketTimeframe {
  return (
    isObject(value) &&
    isCandle(value.lastCandle) &&
    isNullableNumber(value.ema20) &&
    isNullableNumber(value.ema50) &&
    isNullableNumber(value.ema200) &&
    isNullableNumber(value.rsi14) &&
    isNullableNumber(value.atr14) &&
    isNullableNumber(value.volumeRatio) &&
    isOneOf(value.trend, MARKET_TRENDS) &&
    isNumber(value.trendStrength) &&
    isNullableNumber(value.support1) &&
    isNullableNumber(value.support2) &&
    isNullableNumber(value.resistance1) &&
    isNullableNumber(value.resistance2) &&
    isNullableNumber(value.distanceToSupport1Pct) &&
    isNullableNumber(value.distanceToResistance1Pct) &&
    typeof value.rsi14IsReliable === 'boolean' &&
    typeof value.emaIsReliable === 'boolean' &&
    typeof value.emaHasFallback === 'boolean' &&
    typeof value.atrIsReliable === 'boolean' &&
    typeof value.atrIsFallback === 'boolean' &&
    typeof value.volumeRatioIsReliable === 'boolean' &&
    typeof value.volumeRatioIsFallback === 'boolean'
  );
}

function isSentiment(value: unknown): value is MarketSentiment {
  return (
    isObject(value) &&
    isOneOf(value.marketRegime, MARKET_REGIMES) &&
    isNumber(value.longShortBiasScore) &&
    isNumber(value.fundingBiasScore) &&
    isNumber(value.orderBookPressureScore) &&
    isNumber(value.tradeFlowPressureScore)
  );
}

function isPositionMarket(value: unknown): value is PositionMarket {
  return (
    isObject(value) &&
    typeof value.positionId === 'string' &&
    typeof value.exchange === 'string' &&
    isNonEmptyString(value.symbol) &&
    isOneOf(value.marketCategory, MARKET_CATEGORIES) &&
    typeof value.capturedAt === 'string' &&
    isPrice(value.price) &&
    isDerivatives(value.derivatives) &&
    isOrderBook(value.orderBook) &&
    isTradeFlow(value.tradeFlow) &&
    isTimeframe(value.m15) &&
    isTimeframe(value.h1) &&
    isTimeframe(value.h4) &&
    isTimeframe(value.d1) &&
    isSentiment(value.sentiment) &&
    isStringArray(value.tags)
  );
}

function isPositionCandles(value: unknown): value is PositionCandles {
  return (
    isObject(value) &&
    typeof value.positionId === 'string' &&
    typeof value.exchange === 'string' &&
    isNonEmptyString(value.symbol) &&
    isOneOf(value.marketCategory, MARKET_CATEGORIES) &&
    typeof value.interval === 'string' &&
    Array.isArray(value.items) &&
    value.items.every(isCandle)
  );
}

function isPolicyIdentity(value: unknown): value is PolicyIdentity {
  return isObject(value) && typeof value.version === 'string' && typeof value.hash === 'string';
}

function isInputIdentity(value: unknown): value is EvaluationInputIdentity {
  return (
    isObject(value) &&
    typeof value.positionId === 'string' &&
    typeof value.exchangeAccountId === 'string' &&
    typeof value.instrumentId === 'string' &&
    typeof value.positionObservedAt === 'string' &&
    typeof value.portfolioCalculatedAt === 'string' &&
    typeof value.marketCapturedAt === 'string'
  );
}

function isDataQuality(value: unknown): value is AssessmentDataQualityState {
  return (
    isObject(value) &&
    isOneOf(value.market, DATA_QUALITIES) &&
    isOneOf(value.portfolio, DATA_QUALITIES) &&
    isOneOf(value.overall, DATA_QUALITIES) &&
    isOneOf(value.safetyState, SAFETY_STATES)
  );
}

function isTrend(value: unknown): value is AssessmentTrend {
  return (
    isObject(value) &&
    isOneOf(value.marketTrend, MARKET_TRENDS) &&
    isOneOf(value.positionAlignment, TREND_ALIGNMENTS) &&
    isNumber(value.strength) &&
    typeof value.timeframe === 'string'
  );
}

function isMomentum(value: unknown): value is AssessmentMomentum {
  return (
    isObject(value) &&
    isNullableNumber(value.rsi14) &&
    typeof value.isReliable === 'boolean' &&
    isOneOf(value.state, MOMENTUM_STATES) &&
    typeof value.potentialExhaustion === 'boolean'
  );
}

function isVolatility(value: unknown): value is AssessmentVolatility {
  return (
    isObject(value) &&
    isNullableNumber(value.atr14) &&
    isNullableNumber(value.atrPercentOfPrice) &&
    typeof value.isReliable === 'boolean' &&
    typeof value.isFallback === 'boolean'
  );
}

function isLevels(value: unknown): value is AssessmentLevels {
  return (
    isObject(value) &&
    isNullableNumber(value.currentPrice) &&
    isNullableNumber(value.support1) &&
    isNullableNumber(value.distanceToSupport1Percent) &&
    isNullableNumber(value.support1Strength) &&
    isNullableNumber(value.resistance1) &&
    isNullableNumber(value.distanceToResistance1Percent) &&
    isNullableNumber(value.resistance1Strength)
  );
}

function isPnl(value: unknown): value is AssessmentPnl {
  return (
    isObject(value) &&
    isNullableNumber(value.unrealizedPnl) &&
    isNullableNumber(value.pnlPercent) &&
    isNullableNumber(value.averageEntryPrice) &&
    isNullableNumber(value.currentPrice) &&
    isOneOf(value.priceRelativeToEntry, PRICE_POSITIONS) &&
    typeof value.isFavorable === 'boolean'
  );
}

function isStop(value: unknown): value is AssessmentStop {
  return (
    isObject(value) &&
    isNullableNumber(value.stopPrice) &&
    isNullableNumber(value.distanceFromCurrentPercent) &&
    isNullableNumber(value.stopRelativeToEntryPercent) &&
    isOneOf(value.state, STOP_STATES) &&
    isOneOf(value.priceRelativeToEntry, PRICE_POSITIONS) &&
    isNullableNumber(value.trailingStopDistance) &&
    typeof value.hasTrailingStop === 'boolean'
  );
}

function isBreakeven(value: unknown): value is AssessmentBreakeven {
  return (
    isObject(value) &&
    isNullableNumber(value.breakEvenPrice) &&
    isNullableNumber(value.distanceFromCurrentPercent) &&
    isNullableNumber(value.distanceFromEntryPercent) &&
    isOneOf(value.priceRelativeToBreakEven, PRICE_POSITIONS) &&
    typeof value.isProfitable === 'boolean'
  );
}

function isLiquidation(value: unknown): value is AssessmentLiquidation {
  return (
    isObject(value) &&
    isNullableNumber(value.liquidationPrice) &&
    isNullableNumber(value.distanceFromCurrentPercent) &&
    isOneOf(value.state, LIQUIDATION_STATES)
  );
}

function isPortfolioRisk(value: unknown): value is AssessmentPortfolioRisk {
  return (
    isObject(value) &&
    isOneOf(value.policyDecision, RISK_DECISIONS) &&
    isNullableNumber(value.freeCapitalPercent) &&
    isNullableNumber(value.grossExposureToEquityPercent) &&
    isNullableNumber(value.largestPositionConcentrationPercent) &&
    isNullableNumber(value.totalUnrealizedPnl) &&
    isNullableNumber(value.usedCapital) &&
    typeof value.isComplete === 'boolean' &&
    typeof value.isFresh === 'boolean' &&
    isNullableNumber(value.totalEquity) &&
    isNullableNumber(value.availableCapital) &&
    isNullableNumber(value.currentPositionValue) &&
    isNullableNumber(value.currentPositionConcentrationPercent) &&
    isNullableNumber(value.minimumFreeCapitalPercent) &&
    isNullableNumber(value.maximumGrossExposureToEquityPercent) &&
    isNullableNumber(value.maximumPositionConcentrationPercent)
  );
}

function isResult(value: unknown): value is AssessmentResult {
  return (
    isObject(value) &&
    isOneOf(value.positionSide, SIDES) &&
    isNullableNumber(value.currentPrice) &&
    isTrend(value.trend) &&
    isMomentum(value.momentum) &&
    isVolatility(value.volatility) &&
    isLevels(value.levels) &&
    isPnl(value.pnl) &&
    isStop(value.stop) &&
    isBreakeven(value.breakeven) &&
    isLiquidation(value.liquidation) &&
    isPortfolioRisk(value.portfolioRisk)
  );
}

function isAssessment(value: unknown): value is PositionAssessment {
  return (
    isObject(value) &&
    typeof value.id === 'string' &&
    typeof value.evaluatedAt === 'string' &&
    typeof value.validUntil === 'string' &&
    typeof value.ruleVersion === 'string' &&
    typeof value.isLegacy === 'boolean' &&
    isInputIdentity(value.inputIdentity) &&
    isPolicyIdentity(value.basePolicyIdentity) &&
    isPolicyIdentity(value.effectiveConfigurationIdentity) &&
    isOneOf(value.portfolioRiskDecision, RISK_DECISIONS) &&
    isStringArray(value.reasonCodes) &&
    isDataQuality(value.dataQuality) &&
    (value.result === null || isResult(value.result))
  );
}

function isAction(value: unknown): value is RecommendationActionDetails {
  return (
    isObject(value) &&
    isOneOf(value.value, ACTIONS) &&
    isNullableNumber(value.confidence) &&
    (value.priority === null || isOneOf(value.priority, PRIORITIES)) &&
    isStringArray(value.reasonCodes)
  );
}

function isAddConditions(value: unknown): value is RecommendationAddConditions {
  return (
    isObject(value) &&
    isOneOf(value.requiredTrendAlignment, TREND_ALIGNMENTS) &&
    isOneOf(value.requiredMomentumState, MOMENTUM_STATES) &&
    typeof value.protectiveStopRequired === 'boolean' &&
    isNumber(value.minimumLiquidationDistancePercent)
  );
}

function isAddDecision(value: unknown): value is RecommendationAddDecision {
  return (
    isObject(value) &&
    isOneOf(value.value, ADD_DECISIONS) &&
    isStringArray(value.reasonCodes) &&
    isNullableNumber(value.maximumAdditionalPositionValue) &&
    isNullableNumber(value.maximumAdditionalQuantity) &&
    (value.conditions === null || isAddConditions(value.conditions))
  );
}

function isCondition(value: unknown): value is RecommendationCondition {
  return isObject(value) && isOneOf(value.scope, CONDITION_SCOPES) && typeof value.kind === 'string';
}

function isContinuation(value: unknown): value is RecommendationContinuation {
  return (
    isObject(value) &&
    typeof value.nextEvaluationAt === 'string' &&
    Array.isArray(value.invalidationConditions) &&
    value.invalidationConditions.every(isCondition) &&
    Array.isArray(value.reevaluationConditions) &&
    value.reevaluationConditions.every(isCondition)
  );
}

function isRecommendation(value: unknown): value is PositionRecommendation {
  return (
    isObject(value) &&
    typeof value.id === 'string' &&
    typeof value.assessmentId === 'string' &&
    typeof value.createdAt === 'string' &&
    typeof value.validUntil === 'string' &&
    isOneOf(value.status, STATUSES) &&
    isPolicyIdentity(value.policyIdentity) &&
    isAction(value.action) &&
    isAddDecision(value.addDecision) &&
    isStringArray(value.reasonCodes) &&
    (value.continuation === null || isContinuation(value.continuation))
  );
}

function isPositionEvaluation(value: unknown): value is PositionEvaluation {
  return (
    isObject(value) &&
    typeof value.positionId === 'string' &&
    isAssessment(value.assessment) &&
    (value.recommendation === null || isRecommendation(value.recommendation))
  );
}

function isTimelineSnapshot(value: unknown): value is PositionTimelinePositionSnapshot {
  return (
    isObject(value) &&
    isNumber(value.size) &&
    isNullableNumber(value.averageEntryPrice) &&
    isNullableNumber(value.positionValue) &&
    isNullableNumber(value.leverage) &&
    isNullableNumber(value.markPrice) &&
    isNullableNumber(value.breakEvenPrice) &&
    isNullableNumber(value.liquidationPrice) &&
    isNullableNumber(value.unrealizedPnl) &&
    isNullableNumber(value.takeProfit) &&
    isNullableNumber(value.stopLoss) &&
    isNullableNumber(value.trailingStop)
  );
}

function isTimelinePositionChange(value: unknown): value is PositionTimelinePositionChange {
  return (
    isObject(value) &&
    Number.isInteger(value.sequence) &&
    isOneOf(value.kind, CHANGE_KINDS) &&
    isOneOf(value.cause, CHANGE_CAUSES) &&
    isOneOf(value.trackingStateAfter, TRACKING_STATES) &&
    (value.before === null || isTimelineSnapshot(value.before)) &&
    isTimelineSnapshot(value.after)
  );
}

function isTimelineEvaluation(value: unknown): value is PositionTimelineEvaluation {
  return (
    isObject(value) &&
    isNonEmptyString(value.id) &&
    typeof value.evaluatedAt === 'string' &&
    typeof value.validUntil === 'string' &&
    typeof value.ruleVersion === 'string' &&
    typeof value.isLegacy === 'boolean' &&
    isDataQuality(value.dataQuality) &&
    isOneOf(value.portfolioRiskDecision, RISK_DECISIONS) &&
    isStringArray(value.reasonCodes)
  );
}

function isTimelineRecommendation(value: unknown): value is PositionTimelineRecommendation {
  return (
    isObject(value) &&
    isNonEmptyString(value.id) &&
    typeof value.assessmentId === 'string' &&
    typeof value.createdAt === 'string' &&
    typeof value.validUntil === 'string' &&
    isOneOf(value.status, STATUSES) &&
    isOneOf(value.action, ACTIONS) &&
    isNullableNumber(value.confidence) &&
    (value.priority === null || isOneOf(value.priority, PRIORITIES)) &&
    isOneOf(value.addDecision, ADD_DECISIONS) &&
    isStringArray(value.reasonCodes) &&
    typeof value.isLegacy === 'boolean'
  );
}

// Неизвестный type или payload, не соответствующий type, делает всю страницу некорректной:
// частично отображённая история могла бы выдать предположение за факт.
function isTimelineItem(value: unknown): value is PositionTimelineItem {
  if (!isObject(value) || typeof value.occurredAt !== 'string') {
    return false;
  }

  switch (value.type) {
    case 'positionChange':
      return isTimelinePositionChange(value.positionChange) && value.evaluation === null && value.recommendation === null;
    case 'evaluation':
      return value.positionChange === null && isTimelineEvaluation(value.evaluation) && value.recommendation === null;
    case 'recommendation':
      return value.positionChange === null && value.evaluation === null && isTimelineRecommendation(value.recommendation);
    default:
      return false;
  }
}

function isTimelinePage(value: unknown): value is PositionTimelinePage {
  return (
    isObject(value) &&
    Array.isArray(value.items) &&
    value.items.every(isTimelineItem) &&
    isNullableString(value.nextCursor) &&
    typeof value.hasMore === 'boolean' &&
    (!value.hasMore || isNonEmptyString(value.nextCursor))
  );
}
