import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import type { ScreenReader } from '../realtime/autoUpdates';
import { useAutoUpdates, useAutoUpdateState } from '../realtime/autoUpdatesContext';
import { useRealtimeInvalidation } from '../realtime/useRealtimeInvalidation';
import { SIDE_LABELS, formatNumber } from '../portfolio/portfolioFormatting';
import { PositionChart } from '../position/PositionChart';
import { PositionChartData } from '../position/PositionChartData';
import { toChartCandles, toPriceLevels, type ChartCandle } from '../position/positionChartAdapter';
import { PositionDetails } from '../position/PositionDetails';
import { PositionEvaluation } from '../position/PositionEvaluation';
import { PositionMarketContext } from '../position/PositionMarketContext';
import { PositionRecommendation } from '../position/PositionRecommendation';
import { PositionTimeline } from '../position/PositionTimeline';
import {
  PositionApiError,
  evaluatePosition,
  getPosition,
  getPositionCandles,
  getPositionEvaluation,
  getPositionMarket,
} from '../position/positionApi';
import {
  CHART_INTERVALS,
  CHART_INTERVAL_LABELS,
  NOT_EVALUABLE_REASON_MESSAGES,
  parseChartInterval,
} from '../position/positionFormatting';
import { POSITION_SEARCH_PATH, resolveReturnPath } from '../position/positionNavigation';
import { useValidityClock } from '../position/useValidityClock';
import type {
  ChartInterval,
  PositionCandles,
  PositionDetails as Position,
  PositionEvaluation as Evaluation,
  PositionMarket,
} from '../position/positionTypes';

const SESSION_ENDED = 'Сессия завершена. Войдите снова.';
const SERVICE_UNAVAILABLE = 'Сервис временно недоступен. Повторите попытку позже.';
const NO_CANDLES: readonly ChartCandle[] = [];

const ERROR_MESSAGES = {
  market_data_unavailable: 'Рыночные данные временно недоступны. Повторите попытку позже.',
  exchange_unavailable: 'Bybit сейчас недоступен. Повторите попытку позже.',
  access_forbidden: 'Недостаточно прав для просмотра позиции.',
  concurrency_conflict: 'Данные позиции изменились во время операции. Повторите попытку.',
  position_not_evaluable: 'Позиция сейчас не может быть оценена.',
} as const;

type ErrorCode = keyof typeof ERROR_MESSAGES;

interface Notice {
  tone: 'success' | 'error';
  text: string;
  traceId?: string | null;
  sessionEnded?: boolean;
}

/** Результат чтения, привязанный к ключу запроса: данные другого ключа не отображаются. */
type Load<T> = { key: string; value: T | null; error: Notice | null };
type EvaluationLoad = Load<Evaluation | null> & { appliedAt: number | null };

interface EvaluateState {
  running: boolean;
  notice: Notice | null;
}

// UI ветвится только по HTTP status, machine-readable code и reason: title/detail не показываются.
function toErrorNotice(error: unknown, fallback: string): Notice {
  if (!(error instanceof PositionApiError)) {
    return { tone: 'error', text: fallback };
  }

  if (error.status === 401 || error.code === 'authentication_required') {
    return { tone: 'error', text: SESSION_ENDED, sessionEnded: true };
  }

  let text: string | undefined;
  if (error.reason !== null) {
    text = NOT_EVALUABLE_REASON_MESSAGES[error.reason];
  } else if (error.code !== null && Object.hasOwn(ERROR_MESSAGES, error.code)) {
    text = ERROR_MESSAGES[error.code as ErrorCode];
  } else if (error.status === 403) {
    text = ERROR_MESSAGES.access_forbidden;
  } else if (error.status === 503) {
    text = SERVICE_UNAVAILABLE;
  }

  return { tone: 'error', text: text ?? fallback, traceId: error.traceId };
}

function isNotFound(error: unknown): boolean {
  return error instanceof PositionApiError && (error.status === 404 || error.code === 'resource_not_found');
}

function loaded<T>(key: string, value: T): Load<T> {
  return { key, value, error: null };
}

function failed<T>(key: string, error: Notice, previous: Load<T> | null): Load<T> {
  return { key, value: previous?.key === key ? previous.value : null, error };
}

/**
 * Страница позиции. Позиция, рыночный контекст, свечи и оценка загружаются независимо: ошибка
 * одной области не скрывает остальные, а `404` позиции означает недоступную страницу. Каждый
 * результат привязан к `positionId` (свечи — ещё и к интервалу), поэтому поздний ответ прежней
 * позиции или интервала не отображается. Оценка запускается только явной командой пользователя.
 * История позиции загружается своим блоком только после раскрытия и сбрасывается при смене позиции.
 */
export function PositionPage() {
  const { id = '' } = useParams();
  const location = useLocation();
  const { login } = useAuth();
  const updates = useAutoUpdates();
  const { pausedAt } = useAutoUpdateState();
  const [searchParams, setSearchParams] = useSearchParams();
  const rawInterval = searchParams.get('interval');
  const interval = parseChartInterval(rawInterval);
  const candlesKey = `${id}|${interval}`;
  const titleId = useId();
  const chartTitleId = useId();
  const evaluationTitleId = useId();

  const [position, setPosition] = useState<Load<Position> | null>(null);
  const [notFound, setNotFound] = useState<string | null>(null);
  const [market, setMarket] = useState<Load<PositionMarket> | null>(null);
  const [candles, setCandles] = useState<Load<PositionCandles> | null>(null);
  const [evaluation, setEvaluation] = useState<EvaluationLoad | null>(null);
  const [evaluateStates, setEvaluateStates] = useState<Readonly<Record<string, EvaluateState>>>({});

  const positionRead = useRef<ScreenReader | null>(null);
  const marketRead = useRef<ScreenReader | null>(null);
  const candlesRead = useRef<ScreenReader | null>(null);
  const evaluationRead = useRef<ScreenReader | null>(null);
  const positionGeneration = useRef(0);
  const marketGeneration = useRef(0);
  const candlesGeneration = useRef(0);
  const evaluationGeneration = useRef(0);
  const evaluateInFlight = useRef(new Set<string>());
  const currentIdRef = useRef(id);

  useEffect(() => {
    currentIdRef.current = id;
  }, [id]);

  // Неизвестный interval нормализуется до запроса свечей; router state возврата сохраняется.
  useEffect(() => {
    if (rawInterval !== null && rawInterval !== interval) {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current);
          next.set('interval', interval);
          return next;
        },
        { replace: true, state: location.state as unknown },
      );
    }
  }, [rawInterval, interval, setSearchParams, location.state]);

  useEffect(() => {
    const scheduler = updates.createReader((signal, view) => {
      const generation = view.kind === 'silent' ? positionGeneration.current : ++positionGeneration.current;
      const isCurrent = () => generation === positionGeneration.current && !signal.aborted;
      return getPosition(id, signal).then(
      (value) => view.apply(() => {
        if (isCurrent()) {
          setPosition(loaded(id, value));
          setNotFound(null);
        }
      }),
      (error: unknown) => {
        if (!isCurrent()) {
          return;
        }

        view.fail(error, () => {
          if (!isCurrent()) return;
          if (isNotFound(error)) setNotFound(id);
          setPosition((current) => failed(id, toErrorNotice(error, 'Не удалось загрузить позицию. Повторите попытку позже.'), current));
        });
      },
      );
    });
    positionRead.current = scheduler;
    scheduler.refresh();
    return () => { positionRead.current = null; scheduler.dispose(); };
  }, [id, updates]);

  useEffect(() => {
    const scheduler = updates.createReader((signal, view) => {
      const generation = view.kind === 'silent' ? marketGeneration.current : ++marketGeneration.current;
      const isCurrent = () => generation === marketGeneration.current && !signal.aborted;
      return getPositionMarket(id, signal).then(
      (value) => view.apply(() => {
        if (isCurrent()) {
          setMarket(loaded(id, value));
        }
      }),
      (error: unknown) => {
        if (isCurrent()) {
          view.fail(error, () => {
            if (isCurrent()) setMarket((current) => failed(id, toErrorNotice(error, 'Не удалось загрузить рыночный контекст. Повторите попытку позже.'), current));
          });
        }
      },
      );
    });
    marketRead.current = scheduler;
    scheduler.refresh();
    return () => { marketRead.current = null; scheduler.dispose(); };
  }, [id, updates]);

  useEffect(() => {
    const key = `${id}|${interval}`;
    const scheduler = updates.createReader((signal, view) => {
      const generation = view.kind === 'silent' ? candlesGeneration.current : ++candlesGeneration.current;
      const isCurrent = () => generation === candlesGeneration.current && !signal.aborted;
      return getPositionCandles(id, interval, signal).then(
      (value) => view.apply(() => {
        if (isCurrent()) {
          setCandles(loaded(key, value));
        }
      }),
      (error: unknown) => {
        if (isCurrent()) {
          view.fail(error, () => {
            if (isCurrent()) setCandles((current) => failed(key, toErrorNotice(error, 'Не удалось загрузить свечи. Повторите попытку позже.'), current));
          });
        }
      },
      );
    });
    candlesRead.current = scheduler;
    scheduler.refresh();
    return () => { candlesRead.current = null; scheduler.dispose(); };
  }, [id, interval, updates]);

  useEffect(() => {
    const scheduler = updates.createReader((signal, view) => {
      const generation = view.kind === 'silent' ? evaluationGeneration.current : ++evaluationGeneration.current;
      const isCurrent = () => generation === evaluationGeneration.current && !signal.aborted;
      return getPositionEvaluation(id, signal).then(
      (value) => view.apply(() => {
        if (isCurrent()) {
          setEvaluation({ ...loaded(id, value), appliedAt: Date.now() });
        }
      }),
      (error: unknown) => {
        if (isCurrent()) {
          view.fail(error, () => {
            if (isCurrent()) setEvaluation((current) => ({
              ...failed(id, toErrorNotice(error, 'Не удалось загрузить оценку. Повторите попытку позже.'), current),
              appliedAt: current?.key === id ? current.appliedAt : null,
            }));
          });
        }
      },
      );
    });
    evaluationRead.current = scheduler;
    scheduler.refresh();
    return () => { evaluationRead.current = null; scheduler.dispose(); };
  }, [id, updates]);

  useRealtimeInvalidation((event) => {
    if (event.type === 'recovery' || (event.type === 'position.updated' && event.positionId === id)) {
      if (event.type === 'recovery') {
        if (updates.isLive()) positionGeneration.current += 1;
        positionRead.current?.recover();
      }
      else positionRead.current?.invalidate();
    }
    if (event.type === 'recovery') {
      if (updates.isLive()) {
        marketGeneration.current += 1;
        candlesGeneration.current += 1;
      }
      marketRead.current?.recover();
      candlesRead.current?.recover();
    }
    if (event.type === 'recovery' || (event.type === 'evaluation.updated' && event.positionId === id)) {
      if (event.type === 'recovery') {
        if (updates.isLive()) evaluationGeneration.current += 1;
        evaluationRead.current?.recover();
      }
      else evaluationRead.current?.invalidate();
    }
  });

  const currentPosition = position?.key === id ? position : null;
  const currentMarket = market?.key === id ? market : null;
  const currentCandles = candles?.key === candlesKey ? candles : null;
  const currentEvaluation = evaluation?.key === id ? evaluation : null;
  const evaluationValue = currentEvaluation?.value ?? null;
  const evaluateState = evaluateStates[id];
  const now = useValidityClock([
    evaluationValue?.assessment.validUntil,
    evaluationValue?.recommendation?.validUntil,
  ], pausedAt, currentEvaluation?.appliedAt);

  const positionValue = currentPosition?.value ?? null;
  const marketValue = currentMarket?.value ?? null;
  const candlesValue = currentCandles?.value ?? null;
  const chartCandles = useMemo(
    () => (candlesValue === null ? NO_CANDLES : toChartCandles(candlesValue.items)),
    [candlesValue],
  );
  const levels = useMemo(() => toPriceLevels(positionValue, marketValue, interval), [positionValue, marketValue, interval]);

  // Повторную оценку выполняет только пользователь; повторное нажатие во время запроса
  // игнорируется. Результат применяется, только если пользователь остался на той же позиции,
  // и отменяет ещё не завершённое чтение оценки, чтобы оно не перезаписало новый результат.
  const handleEvaluate = async () => {
    const positionId = id;
    if (evaluateInFlight.current.has(positionId)) {
      return;
    }
    updates.cancelResume();

    const update = (state: EvaluateState) => setEvaluateStates((current) => ({ ...current, [positionId]: state }));
    evaluateInFlight.current.add(positionId);
    update({ running: true, notice: null });
    let notice: Notice;
    try {
      const value = await evaluatePosition(positionId);
      if (currentIdRef.current === positionId) {
        evaluationGeneration.current += 1;
        setEvaluation({ ...loaded(positionId, value), appliedAt: Date.now() });
      }

      notice = { tone: 'success', text: 'Оценка выполнена.' };
    } catch (error) {
      notice = toErrorNotice(error, 'Не удалось выполнить оценку. Повторите попытку позже.');
    }

    evaluateInFlight.current.delete(positionId);
    update({ running: false, notice });
  };

  const handleIntervalChange = (next: ChartInterval) => {
    if (next === interval) {
      return;
    }

    setSearchParams(
      (current) => {
        const params = new URLSearchParams(current);
        params.set('interval', next);
        return params;
      },
      { state: location.state as unknown },
    );
  };

  const returnPath = resolveReturnPath(location.state, positionValue?.exchangeAccountId ?? null);
  const backLink = (
    <Link to={returnPath} className="back-link">
      {returnPath.split('?')[0] === POSITION_SEARCH_PATH ? '← К результатам поиска' : '← К списку позиций'}
    </Link>
  );

  if (notFound === id) {
    return (
      <section className="page position-page" aria-labelledby={titleId}>
        {backLink}
        <h1 id={titleId}>Позиция недоступна</h1>
        <p className="muted">Позиция не найдена или недоступна для текущего пользователя.</p>
      </section>
    );
  }

  const symbol = positionValue?.symbol ?? marketValue?.symbol ?? null;
  const title =
    positionValue === null
      ? (symbol ?? 'Позиция')
      : `${positionValue.symbol} · ${formatNumber(positionValue.size)} ${SIDE_LABELS[positionValue.side]}`;

  return (
    <section className="page position-page" aria-labelledby={titleId}>
      {backLink}
      <h1 id={titleId}>{title}</h1>

      {currentPosition === null && (
        <p className="inline-status" role="status">
          <span className="spinner" aria-hidden="true" />
          Загружаем позицию…
        </p>
      )}
      {currentPosition?.error && <NoticeMessage notice={currentPosition.error} onLogin={login} />}
      {positionValue !== null && <PositionDetails position={positionValue} />}

      <section className="panel chart-panel" aria-labelledby={chartTitleId}>
        <header className="chart-header">
          <h2 id={chartTitleId} className="section-title">
            График
          </h2>
          <div className="segmented" role="group" aria-label="Интервал графика">
            {CHART_INTERVALS.map((option) => (
              <button
                key={option}
                type="button"
                className="segmented-option"
                aria-pressed={option === interval}
                onClick={() => handleIntervalChange(option)}
              >
                {CHART_INTERVAL_LABELS[option]}
              </button>
            ))}
          </div>
        </header>
        {currentCandles === null && (
          <p className="inline-status" role="status">
            <span className="spinner" aria-hidden="true" />
            Загружаем свечи…
          </p>
        )}
        {currentCandles?.error && <NoticeMessage notice={currentCandles.error} onLogin={login} />}
        {currentCandles?.value && chartCandles.length === 0 && (
          <p className="muted">Свечей для интервала {CHART_INTERVAL_LABELS[interval]} нет.</p>
        )}
        <PositionChart
          candles={chartCandles}
          levels={levels}
          dataKey={candlesKey}
          label={`Свечной график ${symbol ?? 'позиции'}, интервал ${CHART_INTERVAL_LABELS[interval]}`}
        />
        <PositionChartData
          symbol={symbol ?? 'Позиция'}
          interval={interval}
          candles={chartCandles}
          levels={levels}
          loading={currentCandles === null}
          dataUnavailable={currentCandles?.error != null && currentCandles.value === null}
          showingPreviousData={currentCandles?.error != null && currentCandles.value !== null}
          levelsIncomplete={currentPosition === null || currentMarket === null || currentPosition.error !== null || currentMarket.error !== null}
        />
      </section>

      {currentMarket === null && (
        <p className="inline-status" role="status">
          <span className="spinner" aria-hidden="true" />
          Загружаем рыночный контекст…
        </p>
      )}
      {currentMarket?.error && (
        <section className="panel" aria-label="Рыночный контекст">
          <NoticeMessage notice={currentMarket.error} onLogin={login} />
        </section>
      )}
      {marketValue !== null && <PositionMarketContext market={marketValue} />}

      <section className="panel evaluation-panel" aria-labelledby={evaluationTitleId}>
        <header className="evaluation-header">
          <h2 id={evaluationTitleId} className="section-title">
            Оценка и рекомендация
          </h2>
          <button
            type="button"
            className="button"
            disabled={evaluateState?.running === true}
            onClick={() => void handleEvaluate()}
          >
            {evaluateState?.running === true ? 'Оценка выполняется…' : 'Переоценить'}
          </button>
        </header>
        {evaluateState?.running === true && (
          <p className="inline-status" role="status">
            <span className="spinner" aria-hidden="true" />
            Выполняем оценку позиции…
          </p>
        )}
        {evaluateState?.notice && <NoticeMessage notice={evaluateState.notice} onLogin={login} />}
        {currentEvaluation === null && (
          <p className="inline-status" role="status">
            <span className="spinner" aria-hidden="true" />
            Загружаем оценку…
          </p>
        )}
        {currentEvaluation?.error && <NoticeMessage notice={currentEvaluation.error} onLogin={login} />}
        {currentEvaluation !== null && currentEvaluation.error === null && evaluationValue === null && (
          <p className="muted">Оценка ещё не выполнялась. Запустите её кнопкой «Переоценить».</p>
        )}
        {evaluationValue !== null && (
          <>
            <PositionEvaluation
              assessment={evaluationValue.assessment}
              marketCapturedAt={marketValue?.capturedAt ?? null}
              now={now}
            />
            <PositionRecommendation recommendation={evaluationValue.recommendation} now={now} />
          </>
        )}
      </section>

      {positionValue !== null && (
        <PositionTimeline
          key={id}
          positionId={id}
          settlementAsset={positionValue.settlementAsset}
          onLogin={login}
        />
      )}
    </section>
  );
}

function NoticeMessage({ notice, onLogin }: { notice: Notice; onLogin: () => void }) {
  if (notice.tone === 'success') {
    return (
      <p className="notice notice-success" role="status">
        {notice.text}
      </p>
    );
  }

  return (
    <div className="alert" role="alert">
      <p className="notice-text">{notice.text}</p>
      {notice.traceId && <p className="notice-trace">Код диагностики: {notice.traceId}</p>}
      {notice.sessionEnded && (
        <button type="button" className="button button-secondary" onClick={onLogin}>
          Войти снова
        </button>
      )}
    </div>
  );
}
