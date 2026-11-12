export interface RealtimeObservation {
  status: string;
  lastConnectedAt: string | null;
  lastHeartbeatAt: string | null;
  reasonCode: string | null;
}
export interface FactualFreshness { status: string; lastFullyReconciledAt: string | null }
export interface ValuationFreshness { status: string; calculatedAt: string | null }
export interface MarketValuation {
  markPrice: number | null;
  indicativePositionValue: number | null;
  settlementAsset: string | null;
  source: string | null;
  observedAt: string | null;
  calculatedAt: string;
  freshnessStatus: string;
}

const object = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const nullableString = (value: unknown) => value === null || typeof value === 'string';
const nullableNumber = (value: unknown) => value === null || (typeof value === 'number' && Number.isFinite(value));

export function isRealtimeObservation(value: unknown): value is RealtimeObservation | null | undefined {
  return value == null || (object(value) && typeof value.status === 'string' && nullableString(value.lastConnectedAt)
    && nullableString(value.lastHeartbeatAt) && nullableString(value.reasonCode));
}
export function isFactualFreshness(value: unknown): value is FactualFreshness | null | undefined {
  return value == null || (object(value) && typeof value.status === 'string' && nullableString(value.lastFullyReconciledAt));
}
export function isValuationFreshness(value: unknown): value is ValuationFreshness | null | undefined {
  return value == null || (object(value) && typeof value.status === 'string' && nullableString(value.calculatedAt));
}
export function isMarketValuation(value: unknown): value is MarketValuation | null | undefined {
  return value == null || (object(value) && nullableNumber(value.markPrice) && nullableNumber(value.indicativePositionValue)
    && (value.settlementAsset === null || (typeof value.settlementAsset === 'string' && value.settlementAsset.trim().length > 0))
    && nullableString(value.source) && nullableString(value.observedAt)
    && typeof value.calculatedAt === 'string' && typeof value.freshnessStatus === 'string');
}

const UNKNOWN = 'Не подтверждено';
const OBSERVATION_LABELS: Readonly<Record<string, string>> = {
  unknown: UNKNOWN, initializing: 'Запускается', connected: 'Подключено', reconnecting: 'Переподключение',
  degraded: 'Нарушено', stopped: 'Остановлено',
};
const FACTUAL_LABELS: Readonly<Record<string, string>> = {
  unknown: UNKNOWN, fresh: 'Актуально', stale: 'Устарело', degraded: 'Нарушено',
};
const VALUATION_LABELS: Readonly<Record<string, string>> = {
  unknown: UNKNOWN, fresh: 'Актуально', stale: 'Устарело', partial: 'Частично', notApplicable: 'Не применяется',
};
function label(labels: Readonly<Record<string, string>>, status: string | null | undefined): string {
  return status == null || !Object.hasOwn(labels, status) ? UNKNOWN : labels[status]!;
}
export const observationLabel = (status: string | null | undefined) => label(OBSERVATION_LABELS, status);
export const factualFreshnessLabel = (status: string | null | undefined) => label(FACTUAL_LABELS, status);
export const valuationFreshnessLabel = (status: string | null | undefined) => label(VALUATION_LABELS, status);
