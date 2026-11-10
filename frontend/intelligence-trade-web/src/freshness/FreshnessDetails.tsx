import { formatAssetAmount, formatNumber, formatTimestamp } from '../portfolio/portfolioFormatting';
import { observationLabel, valuationFreshnessLabel, type MarketValuation, type RealtimeObservation } from './freshness';

export function ObservationDetails({ observation }: { observation?: RealtimeObservation | null }) {
  return <div>
    <dt>Наблюдение биржи</dt>
    <dd>{observationLabel(observation?.status)}
      {observation?.lastHeartbeatAt && <> · последнее сообщение: <time dateTime={observation.lastHeartbeatAt}>{formatTimestamp(observation.lastHeartbeatAt)}</time></>}
      {observation?.reasonCode && <> · {observation.reasonCode}</>}
    </dd>
  </div>;
}

export function MarketValuationDetails({ valuation }: { valuation?: MarketValuation | null }) {
  const asset = valuation?.settlementAsset;
  const amount = valuation?.indicativePositionValue ?? null;
  return <section aria-label="Производная рыночная оценка">
    <p><strong>Производная рыночная оценка</strong></p>
    {valuation == null ? <p>Не подтверждена</p> : <dl className="value-rows">
      <div><dt>Рыночная Mark</dt><dd>{formatNumber(valuation.markPrice)}</dd></div>
      <div><dt>Индикативная стоимость</dt><dd>{asset ? formatAssetAmount(amount, asset) : `${formatNumber(amount)} · актив расчёта не указан`}</dd></div>
      <div><dt>Свежесть оценки</dt><dd>{valuationFreshnessLabel(valuation.freshnessStatus)}</dd></div>
      <div><dt>Наблюдение цены</dt><dd>{valuation.observedAt ? <time dateTime={valuation.observedAt}>{formatTimestamp(valuation.observedAt)}</time> : '—'}</dd></div>
      <div><dt>Расчёт</dt><dd><time dateTime={valuation.calculatedAt}>{formatTimestamp(valuation.calculatedAt)}</time></dd></div>
      <div><dt>Источник</dt><dd>{valuation.source ?? '—'}</dd></div>
    </dl>}
  </section>;
}
