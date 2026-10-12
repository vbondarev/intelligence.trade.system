import { describe, expect, it } from 'vitest';
import {
  CLOSED_POSITION_CHANGE,
  INCREASED_POSITION_CHANGE,
  NEW_POSITION_CHANGE,
  REDUCED_POSITION_CHANGE,
  TIMELINE_EVALUATION,
  TIMELINE_RECOMMENDATION,
  positionChangeItem,
  timelineSnapshotFixture,
} from '../test/positionFixtures';
import { reasonCodeLabel } from './positionFormatting';
import {
  CHANGE_CAUSE_LABELS,
  CHANGE_KIND_LABELS,
  TIMELINE_FILTER_LABELS,
  TIMELINE_TYPE_LABELS,
  snapshotRows,
  timelineItemKey,
  timelineItemSummary,
} from './positionTimelineFormatting';

const normalize = (value: string) => value.replace(/\s/g, ' ');

describe('positionTimelineFormatting', () => {
  it('labels all eight observed position change kinds without claiming order execution', () => {
    expect(CHANGE_KIND_LABELS).toEqual({
      new: 'Позиция обнаружена',
      updated: 'Позиция обновлена',
      increased: 'Размер позиции увеличился',
      reduced: 'Размер позиции уменьшился',
      closed: 'Позиция закрыта',
      markedUnknown: 'Состояние позиции не подтверждено',
      markedStale: 'Данные позиции устарели',
      recovered: 'Отслеживание позиции восстановлено',
    });
    const labels = [...Object.values(CHANGE_KIND_LABELS), ...Object.values(CHANGE_CAUSE_LABELS)].join(' ').toLowerCase();
    expect(labels).not.toMatch(/ордер|исполн|сделк|покуп|продаж/);
  });

  it('labels every change cause and every item type', () => {
    expect(Object.keys(CHANGE_CAUSE_LABELS)).toEqual([
      'initialObservation',
      'exchangeObservation',
      'missingFromCompleteObservation',
      'positionsObservationFailed',
      'partialObservation',
      'freshnessExpired',
      'observationRestored',
    ]);
    expect(Object.keys(TIMELINE_TYPE_LABELS)).toEqual(['positionChange', 'evaluation', 'recommendation']);
    expect(Object.keys(TIMELINE_FILTER_LABELS)).toEqual(['positionChange', 'evaluation', 'recommendation']);
  });

  it('builds stable event keys from the source identity, not from the position in the list', () => {
    expect(timelineItemKey(REDUCED_POSITION_CHANGE)).toBe('positionChange:3');
    expect(timelineItemKey(TIMELINE_EVALUATION)).toBe('evaluation:e0000000-0000-4000-8000-000000000001');
    expect(timelineItemKey(TIMELINE_RECOMMENDATION)).toBe('recommendation:c0000000-0000-4000-8000-000000000001');
  });

  it('shows the size change in before → after order for increases and reductions', () => {
    expect(normalize(timelineItemSummary(INCREASED_POSITION_CHANGE))).toBe('Размер позиции увеличился: 0,2 → 0,3');
    expect(normalize(timelineItemSummary(REDUCED_POSITION_CHANGE))).toBe('Размер позиции уменьшился: 0,3 → 0,1');
  });

  it('does not reconstruct the previous size when before is null', () => {
    const increasedWithoutBefore = positionChangeItem('2026-10-12T07:00:00Z', { kind: 'increased', before: null });

    expect(timelineItemSummary(increasedWithoutBefore)).toBe('Размер позиции увеличился');
    expect(timelineItemSummary(NEW_POSITION_CHANGE)).toBe('Позиция обнаружена');
    expect(timelineItemSummary(CLOSED_POSITION_CHANGE)).toBe('Позиция закрыта');
  });

  it('summarizes evaluation by data quality and risk decision and recommendation by its own action only', () => {
    expect(timelineItemSummary(TIMELINE_EVALUATION)).toBe('Данные: устаревшие. Увеличение риска заблокировано.');
    expect(timelineItemSummary(TIMELINE_RECOMMENDATION)).toBe('Наблюдать');
    expect(timelineItemSummary(TIMELINE_RECOMMENDATION)).not.toMatch(/измен/i);
  });

  it('formats snapshot values in the settlement asset and keeps null as a dash', () => {
    const rows = snapshotRows(
      timelineSnapshotFixture({ size: 0, positionValue: null, unrealizedPnl: -15.5, leverage: null, trailingStop: 120.5 }),
      'USDC',
    );
    const byLabel = Object.fromEntries(rows.map((row) => [row.label, normalize(row.value)]));

    expect(rows.map((row) => row.label)).toEqual([
      'Размер',
      'Средняя цена входа',
      'Mark (сохранённая)',
      'Стоимость',
      'Нереализованный PnL',
      'Плечо',
      'Безубыток',
      'Цена ликвидации',
      'Take Profit',
      'Stop Loss',
      'Trailing Stop',
    ]);
    expect(byLabel['Размер']).toBe('0');
    expect(byLabel['Стоимость']).toBe('—');
    expect(byLabel['Нереализованный PnL']).toBe('−15,5 USDC');
    expect(byLabel['Плечо']).toBe('—');
    expect(byLabel['Trailing Stop']).toBe('120,5');
    expect(rows.find((row) => row.label === 'Нереализованный PnL')?.sign).toBe('negative');
    expect(rows.map((row) => row.value).join(' ')).not.toContain('$');
  });

  it('shows unknown reason codes as is', () => {
    expect(reasonCodeLabel('futureReasonCode')).toBe('futureReasonCode');
  });
});
