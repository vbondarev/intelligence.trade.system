import { formatTimestamp } from '../portfolio/portfolioFormatting';
import {
  DATA_QUALITY_LABELS,
  LIQUIDATION_STATE_LABELS,
  MARKET_TREND_LABELS,
  MOMENTUM_STATE_LABELS,
  PRICE_POSITION_LABELS,
  RISK_DECISION_LABELS,
  SAFETY_STATE_LABELS,
  STOP_STATE_LABELS,
  TREND_ALIGNMENT_LABELS,
  formatPercent,
  formatPrice,
  formatScore,
  isLaterThan,
  isPast,
  reasonCodeLabel,
} from './positionFormatting';
import type { AssessmentDataQuality, AssessmentResult, PositionAssessment } from './positionTypes';

interface PositionEvaluationProps {
  assessment: PositionAssessment;
  /** `capturedAt` текущего рыночного контекста страницы, если он загружен. */
  marketCapturedAt: string | null;
  /** Момент, относительно которого проверяется `validUntil`. */
  now: number;
}

function qualityClass(quality: AssessmentDataQuality): string {
  return quality === 'freshCompleteReliable' ? 'data-state-ok' : 'data-state-warning';
}

export function ReasonList({ codes, label }: { codes: readonly string[]; label: string }) {
  if (codes.length === 0) {
    return null;
  }

  return (
    <ul className="reason-list" aria-label={label}>
      {codes.map((code) => (
        <li key={code}>{reasonCodeLabel(code)}</li>
      ))}
    </ul>
  );
}

/**
 * Последняя оценка позиции: время и валидность, использованный рыночный снимок, качество
 * данных, причины и доступный результат. Признаки временной согласованности показываются
 * независимо от backend DataQuality; собственных порогов свежести frontend не вводит.
 */
export function PositionEvaluation({ assessment, marketCapturedAt, now }: PositionEvaluationProps) {
  const expired = isPast(assessment.validUntil, now);
  const marketIsNewer =
    marketCapturedAt !== null && isLaterThan(marketCapturedAt, assessment.inputIdentity.marketCapturedAt);
  const { dataQuality } = assessment;

  return (
    <div className="evaluation">
      <h3 className="subsection-title">Оценка</h3>
      <ul className="data-state" aria-label="Состояние оценки">
        {expired ? (
          <li className="data-state-warning">Оценка утратила временную валидность</li>
        ) : (
          <li className="data-state-ok">Оценка действительна</li>
        )}
        {marketIsNewer && (
          <li className="data-state-warning">Рыночный контекст новее снимка, использованного в оценке</li>
        )}
        {assessment.isLegacy && <li className="data-state-warning">Оценка сохранена в устаревшем формате</li>}
      </ul>

      <dl className="value-rows">
        <div>
          <dt>Оценка выполнена</dt>
          <dd>
            <time dateTime={assessment.evaluatedAt}>{formatTimestamp(assessment.evaluatedAt)}</time>
          </dd>
        </div>
        <div>
          <dt>Действительна до</dt>
          <dd>
            <time dateTime={assessment.validUntil}>{formatTimestamp(assessment.validUntil)}</time>
          </dd>
        </div>
        <div>
          <dt>Снимок рынка в оценке</dt>
          <dd>
            <time dateTime={assessment.inputIdentity.marketCapturedAt}>
              {formatTimestamp(assessment.inputIdentity.marketCapturedAt)}
            </time>
          </dd>
        </div>
        <div>
          <dt>Наблюдение позиции в оценке</dt>
          <dd>
            <time dateTime={assessment.inputIdentity.positionObservedAt}>
              {formatTimestamp(assessment.inputIdentity.positionObservedAt)}
            </time>
          </dd>
        </div>
        <div>
          <dt>Портфель в оценке</dt>
          <dd>
            <time dateTime={assessment.inputIdentity.portfolioCalculatedAt}>
              {formatTimestamp(assessment.inputIdentity.portfolioCalculatedAt)}
            </time>
          </dd>
        </div>
      </dl>

      <h4 className="evaluation-heading">Качество входных данных</h4>
      <ul className="data-state" aria-label="Качество входных данных">
        <li className={qualityClass(dataQuality.overall)}>Итог: {DATA_QUALITY_LABELS[dataQuality.overall]}</li>
        <li className={qualityClass(dataQuality.market)}>Рынок: {DATA_QUALITY_LABELS[dataQuality.market]}</li>
        <li className={qualityClass(dataQuality.portfolio)}>Портфель: {DATA_QUALITY_LABELS[dataQuality.portfolio]}</li>
        <li className={dataQuality.safetyState === 'allowed' ? 'data-state-ok' : 'data-state-warning'}>
          Безопасность: {SAFETY_STATE_LABELS[dataQuality.safetyState]}
        </li>
        <li className={assessment.portfolioRiskDecision === 'allowed' ? 'data-state-ok' : 'data-state-warning'}>
          {RISK_DECISION_LABELS[assessment.portfolioRiskDecision]}
        </li>
      </ul>

      <h4 className="evaluation-heading">Причины и ограничения</h4>
      {assessment.reasonCodes.length === 0 ? (
        <p className="muted">Причины не указаны.</p>
      ) : (
        <ReasonList codes={assessment.reasonCodes} label="Причины оценки" />
      )}

      <h4 className="evaluation-heading">Результат</h4>
      {assessment.result === null ? (
        <p className="muted">
          Результат оценки не сформирован. Выводы о позиции по этой оценке недоступны: см. причины и качество данных.
        </p>
      ) : (
        <AssessmentResultRows result={assessment.result} />
      )}
    </div>
  );
}

function AssessmentResultRows({ result }: { result: AssessmentResult }) {
  const { trend, momentum, volatility, levels, pnl, stop, breakeven, liquidation } = result;

  return (
    <dl className="value-rows">
      <div>
        <dt>Тренд ({trend.timeframe})</dt>
        <dd>
          {MARKET_TREND_LABELS[trend.marketTrend]}, {TREND_ALIGNMENT_LABELS[trend.positionAlignment].toLowerCase()}
        </dd>
      </div>
      <div>
        <dt>Импульс</dt>
        <dd>
          {MOMENTUM_STATE_LABELS[momentum.state]}
          {momentum.rsi14 !== null && `, RSI ${formatScore(momentum.rsi14)}`}
          {!momentum.isReliable && <span className="indicator-flag"> (ненадёжно)</span>}
          {momentum.potentialExhaustion && ', признаки истощения'}
        </dd>
      </div>
      <div>
        <dt>Волатильность (ATR)</dt>
        <dd>
          {formatPercent(volatility.atrPercentOfPrice)}
          {volatility.atrPercentOfPrice !== null && !volatility.isReliable && (
            <span className="indicator-flag"> (ненадёжно)</span>
          )}
        </dd>
      </div>
      <div>
        <dt>Поддержка / сопротивление</dt>
        <dd>
          {formatPrice(levels.support1)} / {formatPrice(levels.resistance1)}
        </dd>
      </div>
      <div>
        <dt>PnL, % / цена к входу</dt>
        <dd>
          {formatPercent(pnl.pnlPercent)} / {PRICE_POSITION_LABELS[pnl.priceRelativeToEntry]}
        </dd>
      </div>
      <div>
        <dt>Стоп</dt>
        <dd>
          {STOP_STATE_LABELS[stop.state]}
          {stop.distanceFromCurrentPercent !== null && `, ${formatPercent(stop.distanceFromCurrentPercent)} от цены`}
        </dd>
      </div>
      <div>
        <dt>Цена к безубытку</dt>
        <dd>{PRICE_POSITION_LABELS[breakeven.priceRelativeToBreakEven]}</dd>
      </div>
      <div>
        <dt>Ликвидация</dt>
        <dd>
          {LIQUIDATION_STATE_LABELS[liquidation.state]}
          {liquidation.distanceFromCurrentPercent !== null &&
            `, ${formatPercent(liquidation.distanceFromCurrentPercent)} от цены`}
        </dd>
      </div>
    </dl>
  );
}
