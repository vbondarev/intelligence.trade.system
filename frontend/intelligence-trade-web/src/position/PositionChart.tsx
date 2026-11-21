import { useEffect, useId, useRef, useState, type FormEvent } from 'react';
import {
  CandlestickSeries,
  ColorType,
  LineStyle,
  createChart,
  type IChartApi,
  type ISeriesApi,
  type LogicalRange,
  type UTCTimestamp,
} from 'lightweight-charts';
import type { ChartCandle, PriceLevel } from './positionChartAdapter';
import { chartPriceScale, formatChartPrice } from './chartPriceFormat';

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
  down: '#ff9c9c',
} as const;

type ChartView = {
  time: { from: UTCTimestamp; to: UTCTimestamp } | null;
  price: { from: number; to: number } | null;
  auto: boolean;
};

type ViewSnapshot = {
  view: ChartView;
  logical: LogicalRange | null;
  revision: number;
  manualTime: boolean;
};

function readView(chart: IChartApi): ChartView {
  const time = chart.timeScale().getVisibleRange();
  const price = chart.priceScale('right');
  return {
    time: time && typeof time.from === 'number' && typeof time.to === 'number'
      ? { from: time.from, to: time.to } : null,
    price: price.getVisibleRange(),
    auto: price.options().autoScale,
  };
}

function restoreView(chart: IChartApi, snapshot: ViewSnapshot, revision: number) {
  const { view, logical } = snapshot;
  if (logical && snapshot.revision === revision) chart.timeScale().setVisibleLogicalRange(logical);
  else if (view.time) chart.timeScale().setVisibleRange(view.time);
  const price = chart.priceScale('right');
  if (!view.auto && view.price) price.setVisibleRange(view.price);
  price.setAutoScale(view.auto);
}

const utc = new Intl.DateTimeFormat('ru-RU', { dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC' });

function viewDescription(view: ChartView | null) {
  if (!view?.time) return 'Диапазон появится после загрузки свечей.';
  const { time, price, auto } = view;
  const dates = `${utc.format(time.from * 1000)} — ${utc.format(time.to * 1000)} UTC`;
  const prices = price ? `Цены ${formatChartPrice(price.from)} — ${formatChartPrice(price.to)}.` : '';
  return `${dates}. ${prices} Автомасштаб цен ${auto ? 'включён' : 'выключен'}.`;
}

/**
 * Свечной график TradingView Lightweight Charts. Chart создаётся один раз на время жизни
 * компонента и удаляется при unmount; данные и price lines обновляются без пересоздания chart.
 * Размер следует за контейнером через встроенный `ResizeObserver` (`autoSize`).
 */
export function PositionChart({ candles, levels, dataKey, label }: PositionChartProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const priceBase = useRef(100);
  const [view, setView] = useState<ChartView | null>(null);
  const [undo, setUndo] = useState<ViewSnapshot | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const [timeError, setTimeError] = useState('');
  const [priceError, setPriceError] = useState('');
  const id = useId();
  const key = useRef<string | null>(null);
  const manualTime = useRef(false);
  const revision = useRef(0);
  const publish = useRef<(announce?: boolean) => void>(() => {});

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
        attributionLogo: false,
      },
      grid: {
        vertLines: { color: COLORS.grid },
        horzLines: { color: COLORS.grid },
      },
      rightPriceScale: { borderColor: COLORS.border },
      timeScale: { borderColor: COLORS.border, timeVisible: true, secondsVisible: false },
      crosshair: {
        vertLine: { labelBackgroundColor: COLORS.border },
        horzLine: { labelBackgroundColor: COLORS.border },
      },
      kineticScroll: { mouse: false, touch: false },
      localization: { locale: 'ru-RU' },
    });
    chartRef.current = chart;
    priceBase.current = 100;
    seriesRef.current = chart.addSeries(CandlestickSeries, {
      priceFormat: { type: 'custom', formatter: formatChartPrice, minMove: 0.01, base: 100 },
      upColor: COLORS.up,
      downColor: COLORS.down,
      wickUpColor: COLORS.up,
      wickDownColor: COLORS.down,
      borderVisible: false,
    });

    let frame = 0;
    let gestureFrame = 0;
    let wheelTimer: ReturnType<typeof setTimeout> | undefined;
    let before: ViewSnapshot | null = null;
    let gestureKey: string | null = null;
    const pointers = new Set<number>();

    publish.current = (announce = false) => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        const current = readView(chart);
        setView(current);
        if (announce) setAnnouncement(viewDescription(current));
      });
    };
    function begin() {
      if (before && gestureKey === key.current) return;
      before = {
        view: readView(chart),
        logical: chart.timeScale().getVisibleLogicalRange(),
        revision: revision.current,
        manualTime: manualTime.current,
      };
      gestureKey = key.current;
    }
    function finish(cancelled = false) {
      const previous = before;
      const previousKey = gestureKey;
      before = null;
      cancelAnimationFrame(gestureFrame);
      gestureFrame = requestAnimationFrame(() => {
        gestureFrame = 0;
        if (!previous || key.current !== previousKey) return;
        if (cancelled) {
          restoreView(chart, previous, revision.current);
          manualTime.current = previous.manualTime;
        }
        const next = readView(chart);
        const logical = chart.timeScale().getVisibleLogicalRange();
        const movedTime = previous.logical?.from !== logical?.from || previous.logical?.to !== logical?.to;
        const changed = movedTime || previous.view.auto !== next.auto ||
          previous.view.price?.from !== next.price?.from || previous.view.price?.to !== next.price?.to;
        if (!cancelled && !changed) return;
        if (!cancelled) {
          if (movedTime) manualTime.current = true;
          setUndo(previous);
        }
        setView(next);
        setAnnouncement(viewDescription(next));
      });
    }
    function pointerDown(event: PointerEvent) {
      if (event.button !== 0) return;
      begin();
      pointers.add(event.pointerId);
    }
    function pointerUp(event: PointerEvent) {
      if (pointers.delete(event.pointerId) && pointers.size === 0) finish();
    }
    function pointerCancel(event: PointerEvent) {
      if (!pointers.has(event.pointerId)) return;
      pointers.clear();
      clearTimeout(wheelTimer);
      finish(true);
    }
    function wheel() {
      begin();
      clearTimeout(wheelTimer);
      wheelTimer = setTimeout(() => { if (pointers.size === 0) finish(); }, 120);
    }
    function doubleClick() {
      if (gestureFrame !== 0) return;
      begin();
      finish();
    }
    container.addEventListener('pointerdown', pointerDown, true);
    container.addEventListener('wheel', wheel, { capture: true, passive: true });
    container.addEventListener('dblclick', doubleClick, true);
    window.addEventListener('pointerup', pointerUp, true);
    window.addEventListener('pointercancel', pointerCancel, true);

    return () => {
      cancelAnimationFrame(frame);
      cancelAnimationFrame(gestureFrame);
      clearTimeout(wheelTimer);
      container.removeEventListener('pointerdown', pointerDown, true);
      container.removeEventListener('wheel', wheel, true);
      container.removeEventListener('dblclick', doubleClick, true);
      window.removeEventListener('pointerup', pointerUp, true);
      window.removeEventListener('pointercancel', pointerCancel, true);
      seriesRef.current = null;
      chartRef.current = null;
      chart.remove();
    };
  }, []);

  useEffect(() => {
    const series = seriesRef.current;
    if (series === null) return;
    const prices = candles.flatMap(({ open, high, low, close }) => [open, high, low, close]);
    prices.push(...levels.map(({ price }) => price));
    const base = 10 ** chartPriceScale(prices);
    if (priceBase.current !== base) {
      series.applyOptions({ priceFormat: { type: 'custom', formatter: formatChartPrice, minMove: 1 / base, base } });
      priceBase.current = base;
      publish.current();
    }
  }, [candles, levels]);

  useEffect(() => {
    const series = seriesRef.current;
    const chart = chartRef.current;
    if (series === null || chart === null) {
      return;
    }

    const sameKey = key.current === dataKey;
    const previous = sameKey && manualTime.current ? readView(chart).time : null;
    key.current = dataKey;
    revision.current += 1;
    series.setData([...candles]);
    if (!sameKey) {
      manualTime.current = false;
      chart.priceScale('right').setAutoScale(true);
    }
    const first = candles[0]?.time;
    const last = candles.at(-1)?.time;
    if (previous && first !== undefined && last !== undefined && previous.to >= first && previous.from <= last) {
      chart.timeScale().setVisibleRange({
        from: Math.max(first, previous.from) as UTCTimestamp,
        to: Math.min(last, previous.to) as UTCTimestamp,
      });
    } else {
      chart.timeScale().fitContent();
    }
    publish.current();
    if (!sameKey) {
      const reset = requestAnimationFrame(() => {
        setUndo(null);
        setAnnouncement('');
        setTimeError('');
        setPriceError('');
      });
      return () => cancelAnimationFrame(reset);
    }
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
        axisLabelTextColor: '#000000',
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

  function changeTime(direction: 'back' | 'forward' | 'in' | 'out') {
    const scale = chartRef.current?.timeScale();
    const range = scale?.getVisibleLogicalRange();
    if (!scale || !range || candles.length < 2) return;
    const available = candles.length - 1;
    const width = Math.max(1, Math.min(available, range.to - range.from));
    const target = Math.max(1, Math.min(available, width * (direction === 'in' ? 0.67 : direction === 'out' ? 1.5 : 1)));
    const step = direction === 'back' ? -width / 2 : direction === 'forward' ? width / 2 : 0;
    const from = Math.max(0, Math.min(available - target, range.from + step + (width - target) / 2));
    scale.setVisibleLogicalRange({ from, to: from + target });
    manualTime.current = true;
    publish.current(true);
  }

  function setRange(event: FormEvent<HTMLFormElement>, kind: 'time' | 'price') {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const parse = (name: string) => {
      const text = String(data.get(name) ?? '').trim();
      if (!text) return NaN;
      return kind === 'time' ? Date.parse(text + 'Z') / 1000 : Number(text.replace(',', '.'));
    };
    const from = parse('from');
    const to = parse('to');
    const chart = chartRef.current;
    if (!chart) return;
    if (kind === 'time') {
      const first = candles[0]?.time;
      const last = candles.at(-1)?.time;
      if (!Number.isFinite(from) || !Number.isFinite(to) || from >= to || first === undefined || last === undefined || from < first || to > last) {
        setTimeError('Укажите начало и конец по UTC внутри периода загруженных свечей. Конец должен быть позже начала.');
        return;
      }
      setTimeError('');
      chart.timeScale().setVisibleRange({ from: from as UTCTimestamp, to: to as UTCTimestamp });
      manualTime.current = true;
    } else {
      if (!Number.isFinite(from) || !Number.isFinite(to) || from < 0 || from >= to) {
        setPriceError('Введите две цены. Минимальная цена должна быть неотрицательной и меньше максимальной.');
        return;
      }
      setPriceError('');
      chart.priceScale('right').setVisibleRange({ from, to });
    }
    publish.current(true);
  }

  const atStart = !view?.time || view.time.from <= (candles[0]?.time ?? Infinity);
  const atEnd = !view?.time || view.time.to >= (candles.at(-1)?.time ?? -Infinity);
  return (
    <>
      <div ref={containerRef} className="position-chart" role="group" aria-label={label} />
      <p className="muted chart-attribution">
        <a href="https://www.tradingview.com/">Графики TradingView</a>{' '}
        <span lang="en">TradingView Lightweight Charts™ · Copyright © 2025 TradingView, Inc.</span>
      </p>
      <details className="chart-controls">
        <summary>Вид графика</summary>
        <p>{viewDescription(view)}</p>
        {candles.length < 2 && <p>Для прокрутки и масштаба нужны как минимум две свечи.</p>}
        {candles.length >= 2 && (atStart || atEnd) && <p>Достигнута {atStart && atEnd ? 'граница с обеих сторон' : atStart ? 'начальная граница' : 'конечная граница'} загруженных данных.</p>}
        <div className="form-actions">
          <button type="button" className="button button-secondary" disabled={atStart} onClick={() => changeTime('back')}>Раньше</button>
          <button type="button" className="button button-secondary" disabled={atEnd} onClick={() => changeTime('forward')}>Позже</button>
          <button type="button" className="button button-secondary" disabled={candles.length < 2} onClick={() => changeTime('in')}>Увеличить масштаб времени</button>
          <button type="button" className="button button-secondary" disabled={atStart && atEnd} onClick={() => changeTime('out')}>Уменьшить масштаб времени</button>
          <button type="button" className="button button-secondary" disabled={!view?.time} onClick={() => {
            chartRef.current?.timeScale().fitContent();
            manualTime.current = false;
            publish.current(true);
          }}>Показать все свечи</button>
        </div>
        <form key={`${dataKey}-time`} className="chart-range" onSubmit={(event) => setRange(event, 'time')}>
          <label className="field">Начало диапазона (UTC)
            <input name="from" type="datetime-local" step="60" aria-invalid={!!timeError} aria-describedby={timeError ? `${id}-time` : undefined} onChange={() => setTimeError('')} />
          </label>
          <label className="field">Конец диапазона (UTC)
            <input name="to" type="datetime-local" step="60" aria-invalid={!!timeError} aria-describedby={timeError ? `${id}-time` : undefined} onChange={() => setTimeError('')} />
          </label>
          {timeError && <p id={`${id}-time`} role="alert">{timeError}</p>}
          <button className="button button-secondary" disabled={candles.length < 2}>Установить диапазон времени</button>
        </form>
        <form key={`${dataKey}-price`} className="chart-range" onSubmit={(event) => setRange(event, 'price')}>
          <p>Цена в единицах шкалы графика. Допускается запятая или точка.</p>
          <label className="field">Минимальная цена
            <input name="from" inputMode="decimal" aria-invalid={!!priceError} aria-describedby={priceError ? `${id}-price` : undefined} onChange={() => setPriceError('')} />
          </label>
          <label className="field">Максимальная цена
            <input name="to" inputMode="decimal" aria-invalid={!!priceError} aria-describedby={priceError ? `${id}-price` : undefined} onChange={() => setPriceError('')} />
          </label>
          {priceError && <p id={`${id}-price`} role="alert">{priceError}</p>}
          <button className="button button-secondary" disabled={!view?.price}>Установить диапазон цен</button>
        </form>
        <div className="form-actions">
          <button type="button" className="button button-secondary" aria-pressed={view?.auto ?? true} onClick={() => {
            chartRef.current?.priceScale('right').setAutoScale(true);
            publish.current(true);
          }}>Автомасштаб цен</button>
          <button type="button" className="button button-secondary" disabled={!undo} onClick={() => {
            if (!undo || !chartRef.current) return;
            restoreView(chartRef.current, undo, revision.current);
            manualTime.current = undo.manualTime;
            setUndo(null);
            publish.current(true);
          }}>Отменить изменение вида</button>
        </div>
      </details>
      <p className="sr-only chart-view-status" role="status">{announcement}</p>
    </>
  );
}
