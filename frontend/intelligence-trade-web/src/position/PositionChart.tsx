import { useEffect, useRef } from 'react';
import {
  CandlestickSeries,
  ColorType,
  LineStyle,
  createChart,
  type IChartApi,
  type ISeriesApi,
} from 'lightweight-charts';
import type { ChartCandle, PriceLevel } from './positionChartAdapter';

interface PositionChartProps {
  candles: readonly ChartCandle[];
  levels: readonly PriceLevel[];
  /** Позиция и интервал: при смене набора свечей масштаб подгоняется под новые данные. */
  dataKey: string;
  label: string;
}

const COLORS = {
  text: '#8b98a8',
  grid: '#1b2430',
  border: '#253041',
  up: '#4ade80',
  down: '#f87171',
} as const;

/**
 * Свечной график TradingView Lightweight Charts. Chart создаётся один раз на время жизни
 * компонента и удаляется при unmount; данные и price lines обновляются без пересоздания chart.
 * Размер следует за контейнером через встроенный `ResizeObserver` (`autoSize`).
 */
export function PositionChart({ candles, levels, dataKey, label }: PositionChartProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);

  useEffect(() => {
    const container = containerRef.current;
    if (container === null) {
      return;
    }

    const chart = createChart(container, {
      autoSize: true,
      layout: {
        background: { type: ColorType.Solid, color: 'transparent' },
        textColor: COLORS.text,
        attributionLogo: true,
      },
      grid: {
        vertLines: { color: COLORS.grid },
        horzLines: { color: COLORS.grid },
      },
      rightPriceScale: { borderColor: COLORS.border },
      timeScale: { borderColor: COLORS.border, timeVisible: true, secondsVisible: false },
      localization: { locale: 'ru-RU' },
    });
    chartRef.current = chart;
    seriesRef.current = chart.addSeries(CandlestickSeries, {
      upColor: COLORS.up,
      downColor: COLORS.down,
      wickUpColor: COLORS.up,
      wickDownColor: COLORS.down,
      borderVisible: false,
    });

    return () => {
      seriesRef.current = null;
      chartRef.current = null;
      chart.remove();
    };
  }, []);

  useEffect(() => {
    const series = seriesRef.current;
    if (series === null) {
      return;
    }

    series.setData([...candles]);
    chartRef.current?.timeScale().fitContent();
  }, [candles, dataKey]);

  useEffect(() => {
    const series = seriesRef.current;
    if (series === null) {
      return;
    }

    const lines = levels.map((level) =>
      series.createPriceLine({
        price: level.price,
        color: level.color,
        lineWidth: 1,
        lineStyle: level.dashed ? LineStyle.Dashed : LineStyle.Solid,
        axisLabelVisible: true,
        title: level.title,
      }),
    );

    return () => {
      // После unmount chart уже удалён вместе с серией: удалять её линии не требуется.
      if (seriesRef.current === series) {
        for (const line of lines) {
          series.removePriceLine(line);
        }
      }
    };
  }, [levels]);

  return <div ref={containerRef} className="position-chart" role="group" aria-label={label} />;
}
