import { MISSING_VALUE } from '../portfolio/portfolioFormatting';
import type {
  AddDecision,
  AssessmentDataQuality,
  AssessmentSafetyState,
  ChartInterval,
  LiquidationState,
  MarketCategory,
  MarketRegime,
  MarketTrend,
  MomentumState,
  PositionAction,
  PositionNotEvaluableReason,
  PricePosition,
  RecommendationConditionScope,
  RecommendationPriority,
  RecommendationStatus,
  RiskIncreaseDecision,
  StopState,
  TrendAlignment,
} from './positionTypes';

const LOCALE = 'ru-RU';

const PRICE = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 8 });
const PERCENT = new Intl.NumberFormat(LOCALE, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const SMALL_PERCENT = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 4 });
const SCORE = new Intl.NumberFormat(LOCALE, { maximumFractionDigits: 2 });
const COMPACT = new Intl.NumberFormat(LOCALE, { notation: 'compact', maximumFractionDigits: 2 });

export const CHART_INTERVALS: readonly ChartInterval[] = ['15m', '1h', '4h', '1d'];

export const DEFAULT_CHART_INTERVAL: ChartInterval = '1h';

export const CHART_INTERVAL_LABELS: Record<ChartInterval, string> = {
  '15m': '15m',
  '1h': '1h',
  '4h': '4h',
  '1d': '1d',
};

/** Нормализует значение URL-параметра `interval`; неизвестное значение заменяется на `1h`. */
export function parseChartInterval(value: string | null): ChartInterval {
  return (CHART_INTERVALS as readonly (string | null)[]).includes(value) ? (value as ChartInterval) : DEFAULT_CHART_INTERVAL;
}

export const MARKET_CATEGORY_LABELS: Record<MarketCategory, string> = {
  linear: 'Linear',
  inverse: 'Inverse',
};

export const MARKET_TREND_LABELS: Record<MarketTrend, string> = {
  unknown: 'Не определён',
  bullish: 'Восходящий',
  bearish: 'Нисходящий',
  sideways: 'Боковой',
};

export const MARKET_REGIME_LABELS: Record<MarketRegime, string> = {
  trending: 'Трендовый',
  volatile: 'Волатильный',
  meanReversion: 'Возврат к среднему',
  neutral: 'Нейтральный',
};

export const ACTION_LABELS: Record<PositionAction, string> = {
  hold: 'Удерживать',
  watch: 'Наблюдать',
  protectProfit: 'Защитить прибыль',
  reduce: 'Сократить позицию',
  close: 'Закрыть позицию',
  moveStop: 'Передвинуть стоп',
  takePartialProfit: 'Зафиксировать часть прибыли',
};

export const PRIORITY_LABELS: Record<RecommendationPriority, string> = {
  low: 'Низкий',
  normal: 'Обычный',
  high: 'Высокий',
  critical: 'Критический',
};

export const RECOMMENDATION_STATUS_LABELS: Record<RecommendationStatus, string> = {
  active: 'Активна',
  acknowledged: 'Принята к сведению',
  dismissed: 'Отклонена',
  superseded: 'Заменена новой',
  expired: 'Истекла',
};

export const ADD_DECISION_LABELS: Record<AddDecision, string> = {
  notEvaluated: 'Не оценивалось',
  doNotAdd: 'Не добавлять',
  addAllowed: 'Добавление допустимо',
};

export const DATA_QUALITY_LABELS: Record<AssessmentDataQuality, string> = {
  freshCompleteReliable: 'Свежие, полные, надёжные',
  stale: 'Устаревшие',
  partial: 'Неполные',
  uncertain: 'Недостоверные',
};

export const SAFETY_STATE_LABELS: Record<AssessmentSafetyState, string> = {
  notEvaluated: 'Не оценено',
  allowed: 'Допустимо',
  blocked: 'Заблокировано',
};

export const RISK_DECISION_LABELS: Record<RiskIncreaseDecision, string> = {
  allowed: 'Увеличение риска допустимо',
  blocked: 'Увеличение риска заблокировано',
};

export const TREND_ALIGNMENT_LABELS: Record<TrendAlignment, string> = {
  aligned: 'По тренду',
  adverse: 'Против тренда',
  flatOrUnknown: 'Тренд не выражен',
};

export const MOMENTUM_STATE_LABELS: Record<MomentumState, string> = {
  normal: 'Нормальный',
  overbought: 'Перекупленность',
  oversold: 'Перепроданность',
  unavailable: 'Недоступен',
};

export const PRICE_POSITION_LABELS: Record<PricePosition, string> = {
  below: 'Ниже',
  at: 'На уровне',
  above: 'Выше',
  unavailable: 'Недоступно',
};

export const STOP_STATE_LABELS: Record<StopState, string> = {
  protective: 'Защитный',
  nonProtective: 'Не защищает',
  unknown: 'Не определён',
  unavailable: 'Не установлен',
};

export const LIQUIDATION_STATE_LABELS: Record<LiquidationState, string> = {
  far: 'Далеко',
  near: 'Близко',
  invalid: 'Некорректна',
  unavailable: 'Недоступна',
};

export const CONDITION_SCOPE_LABELS: Record<RecommendationConditionScope, string> = {
  action: 'Действие',
  addDecision: 'Добавление',
  recommendation: 'Рекомендация',
};

export const NOT_EVALUABLE_REASON_MESSAGES: Record<PositionNotEvaluableReason, string> = {
  closedPosition: 'Закрытая позиция не подлежит повторной оценке.',
  portfolioUnavailable: 'Данные портфеля недоступны для оценки.',
  portfolioInconsistent: 'Данные портфеля противоречивы.',
  temporalInconsistency: 'Временная согласованность данных не позволяет выполнить оценку.',
};

const REASON_CODE_LABELS: Readonly<Record<string, string>> = {
  portfolioDataIncomplete: 'Данные портфеля неполные',
  portfolioDataStale: 'Данные портфеля устарели',
  insufficientFreeCapital: 'Недостаточно свободного капитала',
  grossExposureLimitExceeded: 'Превышен лимит общей экспозиции',
  concentrationLimitExceeded: 'Превышен лимит концентрации',
  riskWithinLimits: 'Риск в пределах лимитов',
  marketDataStale: 'Рыночные данные устарели',
  marketDataPartial: 'Рыночные данные неполные',
  marketDataUncertain: 'Рыночные данные недостоверны',
  portfolioDataUncertain: 'Данные портфеля недостоверны',
  riskIncreaseBlockedByDataQuality: 'Увеличение риска заблокировано качеством данных',
  trendAligned: 'Позиция по тренду',
  trendAdverse: 'Позиция против тренда',
  trendFlatOrUnknown: 'Тренд не выражен',
  momentumNormal: 'Импульс в норме',
  momentumOverbought: 'Перекупленность',
  momentumOversold: 'Перепроданность',
  momentumUnavailable: 'Импульс недоступен',
  momentumExhaustion: 'Признаки истощения импульса',
  volatilityUnavailable: 'Волатильность недоступна',
  supportAvailable: 'Поддержка определена',
  resistanceAvailable: 'Сопротивление определено',
  supportNearby: 'Поддержка рядом',
  resistanceNearby: 'Сопротивление рядом',
  lowVolume: 'Низкий объём',
  pnlPositive: 'PnL положительный',
  pnlNegative: 'PnL отрицательный',
  pnlUnavailable: 'PnL недоступен',
  pnlFlat: 'PnL около нуля',
  stopProtective: 'Стоп защищает позицию',
  stopNonProtective: 'Стоп не защищает позицию',
  stopMissing: 'Стоп не установлен',
  stopUnknown: 'Состояние стопа не определено',
  trailingStopDistanceAvailable: 'Задан trailing stop',
  breakevenProfitable: 'Цена лучше безубытка',
  breakevenUnprofitable: 'Цена хуже безубытка',
  breakevenUnavailable: 'Безубыток недоступен',
  breakevenAt: 'Цена на уровне безубытка',
  liquidationFar: 'Ликвидация далеко',
  liquidationNearby: 'Ликвидация близко',
  liquidationInvalid: 'Некорректная цена ликвидации',
  liquidationUnavailable: 'Цена ликвидации недоступна',
  recommendationLimitedByDataQuality: 'Рекомендация ограничена качеством данных',
  closeConditionMet: 'Выполнено условие закрытия',
  lossReductionConditionMet: 'Выполнено условие сокращения убытка',
  partialProfitConditionMet: 'Выполнено условие частичной фиксации прибыли',
  profitProtectionNeeded: 'Требуется защита прибыли',
  moveStopConditionMet: 'Выполнено условие переноса стопа',
  stopNotProtectingProfit: 'Стоп не защищает прибыль',
  addBlockedByAction: 'Добавление заблокировано рекомендуемым действием',
  addBlockedByPortfolioRisk: 'Добавление заблокировано риском портфеля',
  addBlockedByLiquidation: 'Добавление заблокировано близостью ликвидации',
  addBlockedByTrend: 'Добавление заблокировано трендом',
  addBlockedByMomentum: 'Добавление заблокировано импульсом',
  addBlockedByVolume: 'Добавление заблокировано объёмом',
  addBlockedByStop: 'Добавление заблокировано состоянием стопа',
  addMaximumSizeUnavailable: 'Максимальный размер добавления недоступен',
  addAllowedWithinLimits: 'Добавление допустимо в пределах лимитов',
};

const CONDITION_KIND_LABELS: Readonly<Record<string, string>> = {
  trendAlignment: 'Соответствие тренду',
  momentumReliability: 'Надёжность импульса',
  momentumState: 'Состояние импульса',
  momentumAvailability: 'Доступность импульса',
  momentumExhaustion: 'Истощение импульса',
  stopState: 'Состояние стопа',
  stopAvailability: 'Наличие стопа',
  stopRelativePosition: 'Положение стопа',
  profitProtection: 'Защита прибыли',
  liquidationState: 'Состояние ликвидации',
  liquidationDistance: 'Расстояние до ликвидации',
  liquidationDistanceEligibility: 'Допустимость расстояния до ликвидации',
  pnlThreshold: 'Порог PnL',
  pnlAvailability: 'Доступность PnL',
  higherPriorityActions: 'Более приоритетные действия',
  additionalCapacityEligibility: 'Допустимость дополнительного объёма',
  dataQuality: 'Качество данных',
  safetyState: 'Состояние безопасности',
  portfolioRiskDecision: 'Решение по риску портфеля',
  lowVolume: 'Низкий объём',
  policyIdentity: 'Версия политики',
  addAllowedCapacity: 'Допустимый объём добавления',
  opposingLevel: 'Встречный уровень',
  recommendationExpiry: 'Истечение рекомендации',
  continuationContextUnavailable: 'Контекст продолжения недоступен',
};

/** Подпись machine-readable причины; неизвестный код показывается как есть. */
export function reasonCodeLabel(code: string): string {
  return REASON_CODE_LABELS[code] ?? code;
}

/** Подпись условия продолжения рекомендации; неизвестный код показывается как есть. */
export function conditionKindLabel(kind: string): string {
  return CONDITION_KIND_LABELS[kind] ?? kind;
}

export function formatPrice(value: number | null): string {
  return value === null ? MISSING_VALUE : PRICE.format(value);
}

/** Значение, уже выраженное backend в процентах. */
export function formatPercent(value: number | null): string {
  return value === null ? MISSING_VALUE : `${signed(value, false)}${PERCENT.format(Math.abs(value))} %`;
}

/** Значение в процентах с явным знаком, например изменение цены. */
export function formatSignedPercent(value: number | null): string {
  return value === null ? MISSING_VALUE : `${signed(value, true)}${PERCENT.format(Math.abs(value))} %`;
}

/** Доля `0..1` (например, confidence или long ratio) в процентах. */
export function formatFraction(value: number | null): string {
  return value === null ? MISSING_VALUE : `${SCORE.format(value * 100)} %`;
}

/** Ставка финансирования биржи (доля за период) в процентах с явным знаком. */
export function formatFundingRate(value: number | null): string {
  return value === null ? MISSING_VALUE : `${signed(value, true)}${SMALL_PERCENT.format(Math.abs(value) * 100)} %`;
}

/** Безразмерный показатель (score, ratio, strength) без единиц. */
export function formatScore(value: number | null): string {
  return value === null ? MISSING_VALUE : `${signed(value, false)}${SCORE.format(Math.abs(value))}`;
}

/** Крупные объёмы в компактной записи, например «1,2 млн». */
export function formatCompact(value: number | null): string {
  return value === null ? MISSING_VALUE : COMPACT.format(value);
}

/**
 * `true`, если момент `value` уже наступил относительно `now`. Некорректный timestamp не
 * считается истёкшим: о нём нельзя сделать вывод.
 */
export function isPast(value: string, now: number): boolean {
  const timestamp = new Date(value).getTime();
  return !Number.isNaN(timestamp) && timestamp <= now;
}

/** `true`, если `later` строго позже `earlier`; некорректные значения не сравниваются. */
export function isLaterThan(later: string, earlier: string): boolean {
  const laterTime = new Date(later).getTime();
  const earlierTime = new Date(earlier).getTime();
  return !Number.isNaN(laterTime) && !Number.isNaN(earlierTime) && laterTime > earlierTime;
}

function signed(value: number, explicitPlus: boolean): string {
  if (value < 0) {
    return '−';
  }

  return explicitPlus && value > 0 ? '+' : '';
}
