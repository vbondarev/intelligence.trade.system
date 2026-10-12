import { useId } from 'react';
import { MISSING_VALUE, TRACKING_STATE_LABELS, formatTimestamp } from '../portfolio/portfolioFormatting';
import { ReasonList } from './PositionEvaluation';
import {
  ACTION_LABELS,
  ADD_DECISION_LABELS,
  DATA_QUALITY_LABELS,
  PRIORITY_LABELS,
  RECOMMENDATION_STATUS_LABELS,
  RISK_DECISION_LABELS,
  SAFETY_STATE_LABELS,
  formatFraction,
} from './positionFormatting';
import {
  CHANGE_CAUSE_LABELS,
  CHANGE_KIND_LABELS,
  TIMELINE_TYPE_LABELS,
  snapshotRows,
  timelineItemSummary,
  type TimelineValueRow,
} from './positionTimelineFormatting';
import type {
  AssessmentDataQuality,
  PositionTimelineEvaluation,
  PositionTimelineItem as TimelineItem,
  PositionTimelinePositionChange,
  PositionTimelineRecommendation,
} from './positionTypes';

interface PositionTimelineItemProps {
  item: TimelineItem;
  /** Актив расчёта позиции: им подписываются стоимость и PnL исторических снимков. */
  settlementAsset: string;
}

/**
 * Карточка одного события истории: время, тип и краткий результат, а подробности раскрываются
 * нативным `details`. Показываются только сведения, возвращённые API для этого события.
 */
export function PositionTimelineItem({ item, settlementAsset }: PositionTimelineItemProps) {
  const typeId = useId();
  const summaryId = useId();

  return (
    <article className={`position-timeline-item position-timeline-item-${item.type}`} aria-labelledby={`${typeId} ${summaryId}`}>
      <header className="position-timeline-item-header">
        <span id={typeId} className="position-timeline-type">
          {TIMELINE_TYPE_LABELS[item.type]}
        </span>
        <time className="position-timeline-time" dateTime={item.occurredAt}>
          {formatTimestamp(item.occurredAt)}
        </time>
      </header>
      <h3 id={summaryId} className="position-timeline-summary">
        {timelineItemSummary(item)}
      </h3>
      <details className="position-timeline-details">
        <summary>Подробности</summary>
        {item.type === 'positionChange' && (
          <PositionChangeDetails change={item.positionChange} settlementAsset={settlementAsset} />
        )}
        {item.type === 'evaluation' && <EvaluationDetails evaluation={item.evaluation} />}
        {item.type === 'recommendation' && <RecommendationDetails recommendation={item.recommendation} />}
      </details>
    </article>
  );
}

function ValueRows({ rows, label }: { rows: readonly TimelineValueRow[]; label?: string }) {
  return (
    <dl className="value-rows" aria-label={label}>
      {rows.map((row) => (
        <div key={row.label}>
          <dt>{row.label}</dt>
          <dd className={row.sign ? `value-${row.sign}` : undefined}>{row.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function Reasons({ codes, label }: { codes: readonly string[]; label: string }) {
  return codes.length === 0 ? <p className="muted">Причины не указаны.</p> : <ReasonList codes={codes} label={label} />;
}

function PositionChangeDetails({
  change,
  settlementAsset,
}: {
  change: PositionTimelinePositionChange;
  settlementAsset: string;
}) {
  return (
    <div className="position-timeline-body">
      <p className="muted position-timeline-note">
        Изменение зафиксировано по наблюдению позиции и не является подтверждённым ордером или исполнением.
      </p>
      <ValueRows
        rows={[
          { label: 'Изменение', value: CHANGE_KIND_LABELS[change.kind] },
          { label: 'Причина', value: CHANGE_CAUSE_LABELS[change.cause] },
          { label: 'Состояние после изменения', value: TRACKING_STATE_LABELS[change.trackingStateAfter] },
          { label: 'Номер изменения', value: String(change.sequence) },
        ]}
      />
      <div className="position-timeline-snapshots">
        <section className="position-timeline-snapshot">
          <h4 className="evaluation-heading">До</h4>
          {change.before === null ? (
            <p className="muted">Предыдущее состояние не сохранено.</p>
          ) : (
            <ValueRows rows={snapshotRows(change.before, settlementAsset)} label="Состояние до изменения" />
          )}
        </section>
        <section className="position-timeline-snapshot">
          <h4 className="evaluation-heading">После</h4>
          <ValueRows rows={snapshotRows(change.after, settlementAsset)} label="Состояние после изменения" />
        </section>
      </div>
    </div>
  );
}

function qualityClass(quality: AssessmentDataQuality): string {
  return quality === 'freshCompleteReliable' ? 'data-state-ok' : 'data-state-warning';
}

// Истёкшая validUntil не помечается как проблема: историческая оценка остаётся фактом
// момента её выполнения.
function EvaluationDetails({ evaluation }: { evaluation: PositionTimelineEvaluation }) {
  const { dataQuality } = evaluation;

  return (
    <div className="position-timeline-body">
      <dl className="value-rows">
        <div>
          <dt>Оценка выполнена</dt>
          <dd>
            <time dateTime={evaluation.evaluatedAt}>{formatTimestamp(evaluation.evaluatedAt)}</time>
          </dd>
        </div>
        <div>
          <dt>Действительна до</dt>
          <dd>
            <time dateTime={evaluation.validUntil}>{formatTimestamp(evaluation.validUntil)}</time>
          </dd>
        </div>
        <div>
          <dt>Версия правил</dt>
          <dd translate="no">{evaluation.ruleVersion}</dd>
        </div>
      </dl>
      {evaluation.isLegacy && (
        <ul className="data-state" aria-label="Признаки записи оценки">
          <li className="data-state-warning">Оценка сохранена в устаревшем формате</li>
        </ul>
      )}
      <h4 className="evaluation-heading">Качество входных данных</h4>
      <ul className="data-state" aria-label="Качество входных данных исторической оценки">
        <li className={qualityClass(dataQuality.overall)}>Итог: {DATA_QUALITY_LABELS[dataQuality.overall]}</li>
        <li className={qualityClass(dataQuality.market)}>Рынок: {DATA_QUALITY_LABELS[dataQuality.market]}</li>
        <li className={qualityClass(dataQuality.portfolio)}>Портфель: {DATA_QUALITY_LABELS[dataQuality.portfolio]}</li>
        <li className={dataQuality.safetyState === 'allowed' ? 'data-state-ok' : 'data-state-warning'}>
          Безопасность: {SAFETY_STATE_LABELS[dataQuality.safetyState]}
        </li>
        <li className={evaluation.portfolioRiskDecision === 'allowed' ? 'data-state-ok' : 'data-state-warning'}>
          {RISK_DECISION_LABELS[evaluation.portfolioRiskDecision]}
        </li>
      </ul>
      <h4 className="evaluation-heading">Причины и ограничения</h4>
      <Reasons codes={evaluation.reasonCodes} label="Причины исторической оценки" />
    </div>
  );
}

function RecommendationDetails({ recommendation }: { recommendation: PositionTimelineRecommendation }) {
  return (
    <div className="position-timeline-body">
      <dl className="value-rows">
        <div>
          <dt>Сформирована</dt>
          <dd>
            <time dateTime={recommendation.createdAt}>{formatTimestamp(recommendation.createdAt)}</time>
          </dd>
        </div>
        <div>
          <dt>Действительна до</dt>
          <dd>
            <time dateTime={recommendation.validUntil}>{formatTimestamp(recommendation.validUntil)}</time>
          </dd>
        </div>
        <div>
          <dt>Действие</dt>
          <dd>{ACTION_LABELS[recommendation.action]}</dd>
        </div>
        <div>
          <dt>Текущий сохранённый статус записи</dt>
          <dd>{RECOMMENDATION_STATUS_LABELS[recommendation.status]}</dd>
        </div>
        <div>
          <dt>Уверенность</dt>
          <dd>{formatFraction(recommendation.confidence)}</dd>
        </div>
        <div>
          <dt>Приоритет</dt>
          <dd>{recommendation.priority === null ? MISSING_VALUE : PRIORITY_LABELS[recommendation.priority]}</dd>
        </div>
        <div>
          <dt>Увеличение позиции</dt>
          <dd>{ADD_DECISION_LABELS[recommendation.addDecision]}</dd>
        </div>
        <div>
          <dt>Оценка-основание</dt>
          <dd className="position-timeline-identifier" translate="no">
            {recommendation.assessmentId}
          </dd>
        </div>
      </dl>
      {recommendation.isLegacy && (
        <ul className="data-state" aria-label="Признаки записи рекомендации">
          <li className="data-state-warning">Рекомендация сохранена в устаревшем формате</li>
        </ul>
      )}
      <h4 className="evaluation-heading">Причины</h4>
      <Reasons codes={recommendation.reasonCodes} label="Причины исторической рекомендации" />
    </div>
  );
}
