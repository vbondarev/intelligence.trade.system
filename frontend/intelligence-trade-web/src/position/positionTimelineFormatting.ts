import {
  formatAssetAmount,
  formatLeverage,
  formatNumber,
  formatSignedAssetAmount,
} from '../portfolio/portfolioFormatting';
import { ACTION_LABELS, DATA_QUALITY_LABELS, RISK_DECISION_LABELS, formatPrice } from './positionFormatting';
import type {
  PositionChangeCause,
  PositionChangeKind,
  PositionTimelineItem,
  PositionTimelineItemType,
  PositionTimelinePositionChange,
  PositionTimelinePositionSnapshot,
} from './positionTypes';

export const TIMELINE_TYPE_LABELS: Record<PositionTimelineItemType, string> = {
  positionChange: 'Изменение позиции',
  evaluation: 'Оценка позиции',
  recommendation: 'Рекомендация',
};

/** Подписи фильтра истории во множественном числе. */
export const TIMELINE_FILTER_LABELS: Record<PositionTimelineItemType, string> = {
  positionChange: 'Изменения позиции',
  evaluation: 'Оценки',
  recommendation: 'Рекомендации',
};

/** Подписи наблюдаемых изменений: ни одна из них не утверждает факт исполнения ордера. */
export const CHANGE_KIND_LABELS: Record<PositionChangeKind, string> = {
  new: 'Позиция обнаружена',
  updated: 'Позиция обновлена',
  increased: 'Размер позиции увеличился',
  reduced: 'Размер позиции уменьшился',
  closed: 'Позиция закрыта',
  markedUnknown: 'Состояние позиции не подтверждено',
  markedStale: 'Данные позиции устарели',
  recovered: 'Отслеживание позиции восстановлено',
};

export const CHANGE_CAUSE_LABELS: Record<PositionChangeCause, string> = {
  initialObservation: 'Первое наблюдение позиции',
  exchangeObservation: 'Наблюдение данных биржи',
  missingFromCompleteObservation: 'Позиция отсутствует в полном наблюдении биржи',
  positionsObservationFailed: 'Не удалось получить позиции с биржи',
  partialObservation: 'Неполное наблюдение биржи',
  freshnessExpired: 'Истёк срок свежести данных',
  observationRestored: 'Наблюдение восстановлено',
};

export interface TimelineValueRow {
  label: string;
  value: string;
  /** Знак значения для цветового выделения; `null` — без выделения. */
  sign?: 'positive' | 'negative' | null;
}

/**
 * Идентичность события внутри одной позиции: используется как React key и для исключения
 * повторно полученных записей при продолжении.
 */
export function timelineItemKey(item: PositionTimelineItem): string {
  switch (item.type) {
    case 'positionChange':
      return `positionChange:${item.positionChange.sequence}`;
    case 'evaluation':
      return `evaluation:${item.evaluation.id}`;
    case 'recommendation':
      return `recommendation:${item.recommendation.id}`;
  }
}

/** Краткий результат события для свёрнутой карточки. */
export function timelineItemSummary(item: PositionTimelineItem): string {
  switch (item.type) {
    case 'positionChange':
      return positionChangeSummary(item.positionChange);
    case 'evaluation':
      return `Данные: ${DATA_QUALITY_LABELS[item.evaluation.dataQuality.overall].toLowerCase()}. ${
        RISK_DECISION_LABELS[item.evaluation.portfolioRiskDecision]
      }.`;
    case 'recommendation':
      return ACTION_LABELS[item.recommendation.action];
  }
}

// Изменение размера показывается только при сохранённом прежнем состоянии: без него
// исходный размер не восстанавливается.
function positionChangeSummary(change: PositionTimelinePositionChange): string {
  const label = CHANGE_KIND_LABELS[change.kind];
  if ((change.kind === 'increased' || change.kind === 'reduced') && change.before !== null) {
    return `${label}: ${formatNumber(change.before.size)} → ${formatNumber(change.after.size)}`;
  }

  return label;
}

/**
 * Значения сохранённого снимка позиции в фиксированном порядке. Стоимость и PnL подписаны
 * активом расчёта позиции и не являются USD или реализованным результатом сделки.
 */
export function snapshotRows(snapshot: PositionTimelinePositionSnapshot, settlementAsset: string): TimelineValueRow[] {
  return [
    { label: 'Размер', value: formatNumber(snapshot.size) },
    { label: 'Средняя цена входа', value: formatPrice(snapshot.averageEntryPrice) },
    { label: 'Mark (сохранённая)', value: formatPrice(snapshot.markPrice) },
    { label: 'Стоимость', value: formatAssetAmount(snapshot.positionValue, settlementAsset) },
    {
      label: 'Нереализованный PnL',
      value: formatSignedAssetAmount(snapshot.unrealizedPnl, settlementAsset),
      sign: signOf(snapshot.unrealizedPnl),
    },
    { label: 'Плечо', value: formatLeverage(snapshot.leverage) },
    { label: 'Безубыток', value: formatPrice(snapshot.breakEvenPrice) },
    { label: 'Цена ликвидации', value: formatPrice(snapshot.liquidationPrice) },
    { label: 'Take Profit', value: formatPrice(snapshot.takeProfit) },
    { label: 'Stop Loss', value: formatPrice(snapshot.stopLoss) },
    { label: 'Trailing Stop', value: formatPrice(snapshot.trailingStop) },
  ];
}

function signOf(value: number | null): 'positive' | 'negative' | null {
  if (value === null || value === 0) {
    return null;
  }

  return value > 0 ? 'positive' : 'negative';
}
