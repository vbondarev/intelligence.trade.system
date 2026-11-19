import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import type { UTCTimestamp } from 'lightweight-charts';
import { PositionChartData } from './PositionChartData';
import type { ChartCandle, PriceLevel } from './positionChartAdapter';

const candles: ChartCandle[] = Array.from({ length: 200 }, (_, index) => ({
  time: (1_700_000_000 + index * 3600) as UTCTimestamp,
  open: 100,
  high: 103,
  low: 99,
  close: 102,
}));
const levels: PriceLevel[] = [
  { kind: 'entry', title: 'Entry', price: 100, color: '#fff', dashed: false },
  { kind: 'stopLoss', title: 'SL', price: 90, color: '#fff', dashed: false },
  { kind: 'breakEven', title: 'BreakEven', price: 101, color: '#fff', dashed: true },
  { kind: 'takeProfit', title: 'TP', price: 120, color: '#fff', dashed: false },
  { kind: 'liquidation', title: 'Liquidation', price: 80, color: '#fff', dashed: false },
  { kind: 'support', title: 'Support', price: 95, color: '#fff', dashed: true },
  { kind: 'resistance', title: 'Resistance', price: 110, color: '#fff', dashed: true },
];
const props = { symbol: 'BTCUSDT', interval: '1h' as const, candles, levels, loading: false, dataUnavailable: false, showingPreviousData: false, levelsIncomplete: false };

describe('PositionChartData', () => {
  it('сохраняет отличия малых OHLC и ценовых уровней', () => {
    render(<PositionChartData
      {...props}
      candles={[{ ...candles[0]!, open: 1e-9, high: 4e-9, low: 2e-9, close: 3e-9 }]}
      levels={[{ ...levels[1]!, price: 2e-9 }]}
    />);
    screen.getByText('Данные графика и ценовые уровни').click();
    const table = screen.getByRole('table', { name: /Свечи BTCUSDT/ });
    expect(within(table).getAllByRole('cell').map((cell) => cell.textContent))
      .toEqual(['0,000000001', '0,000000004', '0,000000002', '0,000000003']);
    expect(screen.getByRole('rowheader', { name: 'Stop Loss' }).nextElementSibling).toHaveTextContent('0,000000002');
  });

  it('renders all shared candles and labeled price levels', async () => {
    render(<PositionChartData {...props} />);
    await userEvent.setup().click(screen.getByText('Данные графика и ценовые уровни'));
    const table = screen.getByRole('table', { name: /Свечи BTCUSDT, 1h, UTC/ });
    expect(within(table).getAllByRole('row')).toHaveLength(201);
    expect(within(table).getAllByRole('row')[1]).toHaveTextContent('2023-11-14 22:13:20 UTC');
    const firstRow = within(table).getAllByRole('row')[1]!;
    expect(within(firstRow).getAllByRole('cell').map((cell) => cell.textContent)).toEqual(['100', '103', '99', '102']);
    for (const label of ['Цена входа', 'Безубыток', 'Stop Loss', 'Take Profit', 'Ликвидация', 'Поддержка', 'Сопротивление']) {
      expect(screen.getByRole('rowheader', { name: label })).toBeVisible();
    }
  });

  it('distinguishes loading, unavailable, empty and previous data', async () => {
    const { rerender } = render(<PositionChartData {...props} candles={[]} levels={[]} loading />);
    await userEvent.setup().click(screen.getByText('Данные графика и ценовые уровни'));
    expect(screen.getByText('Свечи загружаются.')).toBeVisible();
    expect(screen.queryByText('Свечей для выбранного интервала нет.')).not.toBeInTheDocument();
    rerender(<PositionChartData {...props} candles={[]} levels={[]} dataUnavailable />);
    expect(screen.getByText('Данные свечей недоступны.')).toBeVisible();
    rerender(<PositionChartData {...props} candles={[]} levels={[]} />);
    expect(screen.getByText('Свечей для выбранного интервала нет.')).toBeVisible();
    expect(screen.getByText('Ценовые уровни отсутствуют.')).toBeVisible();
    rerender(<PositionChartData {...props} showingPreviousData levelsIncomplete />);
    expect(screen.getByText(/Показаны ранее полученные свечи/)).toBeVisible();
    expect(screen.getByText(/Актуальность и полнота уровней не подтверждены/)).toBeVisible();
  });
});
