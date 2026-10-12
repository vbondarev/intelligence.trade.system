import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { evaluationFixture } from '../test/positionFixtures';
import { PositionEvaluation } from './PositionEvaluation';
import { PositionRecommendation } from './PositionRecommendation';
import type { PositionAction, PositionRecommendation as Recommendation } from './positionTypes';

const NOW = Date.parse('2026-10-12T10:00:00Z');
const MARKET_AT_INPUT = '2026-10-12T09:00:30Z';

function assessment(overrides: Partial<ReturnType<typeof evaluationFixture>['assessment']> = {}) {
  return { ...evaluationFixture().assessment, ...overrides };
}

function recommendation(overrides: Partial<Recommendation> = {}): Recommendation {
  const base = evaluationFixture().recommendation;
  if (base === null) {
    throw new Error('Fixture must contain a recommendation.');
  }

  return { ...base, ...overrides };
}

function states(label: string): string[] {
  return within(screen.getByRole('list', { name: label }))
    .getAllByRole('listitem')
    .map((item) => item.textContent ?? '');
}

describe('PositionEvaluation', () => {
  it('shows a valid assessment with input identity, data quality, reasons and result', () => {
    render(<PositionEvaluation assessment={assessment()} marketCapturedAt={MARKET_AT_INPUT} now={NOW} />);

    expect(states('Состояние оценки')).toEqual(['Оценка действительна']);
    expect(screen.getByText('Снимок рынка в оценке')).toBeVisible();
    expect(screen.getByText('Наблюдение позиции в оценке')).toBeVisible();
    expect(screen.getByText('Портфель в оценке')).toBeVisible();
    expect(states('Качество входных данных')).toHaveLength(5);
    expect(within(screen.getByRole('list', { name: 'Причины оценки' })).getByText('Риск в пределах лимитов')).toBeVisible();
    expect(screen.getByText('Тренд (1h)')).toBeVisible();
    expect(screen.queryByText(/Результат оценки не сформирован/)).not.toBeInTheDocument();
  });

  it('marks an expired assessment without hiding its data', () => {
    render(
      <PositionEvaluation
        assessment={assessment({ validUntil: '2026-10-12T09:30:00Z' })}
        marketCapturedAt={MARKET_AT_INPUT}
        now={NOW}
      />,
    );

    expect(states('Состояние оценки')).toEqual(['Оценка утратила временную валидность']);
    expect(screen.getByText('Тренд (1h)')).toBeVisible();
  });

  it('marks a market context newer than the snapshot used by the assessment', () => {
    render(<PositionEvaluation assessment={assessment()} marketCapturedAt="2026-10-12T09:05:00Z" now={NOW} />);

    expect(states('Состояние оценки')).toContain('Рыночный контекст новее снимка, использованного в оценке');
  });

  it('does not mark the market context when it is not newer or not loaded', () => {
    const { rerender } = render(
      <PositionEvaluation assessment={assessment()} marketCapturedAt="2026-10-12T08:00:00Z" now={NOW} />,
    );
    expect(states('Состояние оценки')).toEqual(['Оценка действительна']);

    rerender(<PositionEvaluation assessment={assessment()} marketCapturedAt={null} now={NOW} />);
    expect(states('Состояние оценки')).toEqual(['Оценка действительна']);
  });

  it('shows a legacy assessment with a null result as unavailable conclusions', () => {
    render(
      <PositionEvaluation
        assessment={assessment({
          isLegacy: true,
          result: null,
          reasonCodes: ['marketDataStale', 'someFutureReason'],
          dataQuality: {
            market: 'stale',
            portfolio: 'freshCompleteReliable',
            overall: 'stale',
            safetyState: 'blocked',
          },
        })}
        marketCapturedAt={null}
        now={NOW}
      />,
    );

    expect(states('Состояние оценки')).toContain('Оценка сохранена в устаревшем формате');
    expect(screen.getByText(/Результат оценки не сформирован/)).toBeVisible();
    expect(screen.queryByText(/Тренд/)).not.toBeInTheDocument();
    const reasons = within(screen.getByRole('list', { name: 'Причины оценки' }));
    expect(reasons.getByText('Рыночные данные устарели')).toBeVisible();
    expect(reasons.getByText('someFutureReason')).toBeVisible();
  });
});

describe('PositionRecommendation', () => {
  it('shows a missing recommendation without presenting it as Hold', () => {
    render(<PositionRecommendation recommendation={null} now={NOW} />);

    expect(screen.getByText(/Рекомендации для этой оценки нет/)).toBeVisible();
    expect(screen.queryByText('Удерживать')).not.toBeInTheDocument();
  });

  it.each<[PositionAction, string]>([
    ['hold', 'Удерживать'],
    ['watch', 'Наблюдать'],
    ['protectProfit', 'Защитить прибыль'],
    ['reduce', 'Сократить позицию'],
    ['close', 'Закрыть позицию'],
    ['moveStop', 'Передвинуть стоп'],
    ['takePartialProfit', 'Зафиксировать часть прибыли'],
  ])('shows the %s action from the backend', (value, label) => {
    const base = recommendation();
    render(<PositionRecommendation recommendation={{ ...base, action: { ...base.action, value } }} now={NOW} />);

    expect(screen.getByText(label)).toHaveClass(`recommendation-action-${value}`);
  });

  it('shows priority, confidence, reasons, add decision and continuation', () => {
    render(<PositionRecommendation recommendation={recommendation()} now={NOW} />);

    expect(screen.getByText('Приоритет').nextElementSibling).toHaveTextContent('Обычный');
    expect(screen.getByText('Уверенность').nextElementSibling).toHaveTextContent(/70/);
    expect(within(screen.getByRole('list', { name: 'Причины действия' })).getByText('Позиция по тренду')).toBeVisible();
    expect(screen.queryByRole('list', { name: 'Причины рекомендации' })).not.toBeInTheDocument();
    expect(
      within(screen.getByRole('list', { name: 'Причины решения об увеличении' })).getByText(
        'Добавление заблокировано рекомендуемым действием',
      ),
    ).toBeVisible();
    expect(screen.getByText('Ориентир повторной оценки')).toBeVisible();
    expect(screen.getByText(/автоматически повторная оценка не запускается/)).toBeVisible();
    expect(within(screen.getByRole('list', { name: 'Условия утраты силы' })).getByText(/Соответствие тренду/)).toBeVisible();
    expect(
      within(screen.getByRole('list', { name: 'Условия повторной оценки' })).getByText(/Истечение рекомендации/),
    ).toBeVisible();
    expect(states('Состояние рекомендации')).toHaveLength(1);
  });

  it('hides absent priority and confidence and shows missing continuation', () => {
    const base = recommendation();
    render(
      <PositionRecommendation
        recommendation={{
          ...base,
          action: { ...base.action, priority: null, confidence: null },
          continuation: null,
        }}
        now={NOW}
      />,
    );

    expect(screen.queryByText('Приоритет')).not.toBeInTheDocument();
    expect(screen.queryByText('Уверенность')).not.toBeInTheDocument();
    expect(screen.getByText('Условия пересмотра не переданы.')).toBeVisible();
  });

  it('marks an expired recommendation', () => {
    render(<PositionRecommendation recommendation={recommendation({ validUntil: '2026-10-12T09:30:00Z' })} now={NOW} />);

    expect(states('Состояние рекомендации')).toContain('Рекомендация утратила временную валидность');
  });

  it('shows add limits when the backend provides them', () => {
    const base = recommendation();
    render(
      <PositionRecommendation
        recommendation={{
          ...base,
          addDecision: {
            ...base.addDecision,
            value: 'addAllowed',
            maximumAdditionalPositionValue: 1500,
            maximumAdditionalQuantity: 0.02,
          },
        }}
        now={NOW}
      />,
    );

    expect(screen.getByText('Максимальная добавочная стоимость')).toBeVisible();
    expect(screen.getByText('Максимальный добавочный объём')).toBeVisible();
  });
});
