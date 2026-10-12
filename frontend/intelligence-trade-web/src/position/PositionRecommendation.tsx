import { formatNumber, formatTimestamp } from '../portfolio/portfolioFormatting';
import { ReasonList } from './PositionEvaluation';
import {
  ACTION_LABELS,
  ADD_DECISION_LABELS,
  CONDITION_SCOPE_LABELS,
  PRIORITY_LABELS,
  RECOMMENDATION_STATUS_LABELS,
  conditionKindLabel,
  formatFraction,
  isPast,
} from './positionFormatting';
import type { PositionRecommendation as Recommendation, RecommendationCondition } from './positionTypes';

interface PositionRecommendationProps {
  /** `null`: рекомендации нет; это не равнозначно действию «Удерживать». */
  recommendation: Recommendation | null;
  now: number;
}

function ConditionList({ conditions, label }: { conditions: readonly RecommendationCondition[]; label: string }) {
  if (conditions.length === 0) {
    return <p className="muted">Не указаны.</p>;
  }

  return (
    <ul className="reason-list" aria-label={label}>
      {conditions.map((condition) => (
        <li key={`${condition.scope}:${condition.kind}`}>
          {CONDITION_SCOPE_LABELS[condition.scope]}: {conditionKindLabel(condition.kind)}
        </li>
      ))}
    </ul>
  );
}

/**
 * Рекомендация последней оценки. Frontend не формирует рекомендации: действие, приоритет,
 * уверенность, причины и условия пересмотра показываются только из ответа backend.
 */
export function PositionRecommendation({ recommendation, now }: PositionRecommendationProps) {
  if (recommendation === null) {
    return (
      <div className="recommendation">
        <h3 className="subsection-title">Рекомендация</h3>
        <p className="muted">Рекомендации для этой оценки нет. Отсутствие рекомендации не означает «Удерживать».</p>
      </div>
    );
  }

  const { action, addDecision, continuation } = recommendation;
  const expired = isPast(recommendation.validUntil, now);

  return (
    <div className="recommendation">
      <h3 className="subsection-title">Рекомендация</h3>
      <p className={`recommendation-action recommendation-action-${action.value}`}>{ACTION_LABELS[action.value]}</p>
      <ul className="data-state" aria-label="Состояние рекомендации">
        <li>{RECOMMENDATION_STATUS_LABELS[recommendation.status]}</li>
        {expired && <li className="data-state-warning">Рекомендация утратила временную валидность</li>}
      </ul>
      <dl className="value-rows">
        {action.priority !== null && (
          <div>
            <dt>Приоритет</dt>
            <dd>{PRIORITY_LABELS[action.priority]}</dd>
          </div>
        )}
        {action.confidence !== null && (
          <div>
            <dt>Уверенность</dt>
            <dd>{formatFraction(action.confidence)}</dd>
          </div>
        )}
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
      </dl>

      <h4 className="evaluation-heading">Причины действия</h4>
      {action.reasonCodes.length === 0 && recommendation.reasonCodes.length === 0 ? (
        <p className="muted">Причины не указаны.</p>
      ) : (
        <>
          <ReasonList codes={action.reasonCodes} label="Причины действия" />
          <ReasonList
            codes={recommendation.reasonCodes.filter((code) => !action.reasonCodes.includes(code))}
            label="Причины рекомендации"
          />
        </>
      )}

      <h4 className="evaluation-heading">Увеличение позиции</h4>
      <p className="add-decision">{ADD_DECISION_LABELS[addDecision.value]}</p>
      <ReasonList codes={addDecision.reasonCodes} label="Причины решения об увеличении" />
      {(addDecision.maximumAdditionalPositionValue !== null || addDecision.maximumAdditionalQuantity !== null) && (
        <dl className="value-rows">
          {addDecision.maximumAdditionalPositionValue !== null && (
            <div>
              <dt>Максимальная добавочная стоимость</dt>
              <dd>{formatNumber(addDecision.maximumAdditionalPositionValue)}</dd>
            </div>
          )}
          {addDecision.maximumAdditionalQuantity !== null && (
            <div>
              <dt>Максимальный добавочный объём</dt>
              <dd>{formatNumber(addDecision.maximumAdditionalQuantity)}</dd>
            </div>
          )}
        </dl>
      )}

      <h4 className="evaluation-heading">Пересмотр</h4>
      {continuation === null ? (
        <p className="muted">Условия пересмотра не переданы.</p>
      ) : (
        <>
          <dl className="value-rows">
            <div>
              <dt>Ориентир повторной оценки</dt>
              <dd>
                <time dateTime={continuation.nextEvaluationAt}>{formatTimestamp(continuation.nextEvaluationAt)}</time>
              </dd>
            </div>
          </dl>
          <p className="muted continuation-note">
            Это ориентир: автоматически повторная оценка не запускается, её выполняет команда «Переоценить».
          </p>
          <h5 className="condition-heading">Условия, при нарушении которых рекомендация утрачивает силу</h5>
          <ConditionList conditions={continuation.invalidationConditions} label="Условия утраты силы" />
          <h5 className="condition-heading">Условия, требующие повторной оценки</h5>
          <ConditionList conditions={continuation.reevaluationConditions} label="Условия повторной оценки" />
        </>
      )}
    </div>
  );
}
