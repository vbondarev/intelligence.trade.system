import { presentError } from '../errors/errorPresentation';
import { StatusMessage } from '../ui/StatusMessage';
import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import { resourceUnavailable, type ScreenReader } from '../realtime/autoUpdates';
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

const NO_CANDLES: readonly ChartCandle[] = [];

const ERROR_MESSAGES = {
  market_data_unavailable: 'Рыночные данные временно недоступны. Повторите попытку позже.',
  exchange_unavailable: 'Bybit сейчас недоступен. Повторите попытку позже.',
  access_forbidden: 'Недостаточно прав для просмотра позиции.',
  concurrency_conflict: 'Данные позиции изменились во время операции. Повторите попытку.',
  position_not_evaluable: 'Позиция сейчас не может быть оценена.',
} as const;


interface Notice {
  tone: 'success' | 'error';
  text: string;
  traceId?: string | null;
  sessionEnded?: boolean;
  background?: boolean;
  accessDenied?: boolean;
}

/** Результат чтения, привязанный к ключу запроса: данные другого ключа не отображаются. */
type Load<T> = { key: string; value: T | null; error: Notice | null };
type EvaluationLoad = Load<Evaluation | null> & { appliedAt: number | null };

interface EvaluateState {
  running: boolean;
  notice: Notice | null;
}

// UI ветвится только по HTTP status, machine-readable code и reason: title/detail не показываются.
function toErrorNotice(error: unknown, fallback: string, background = false): Notice {
  const errorNotice = presentError(error, { fallback: fallback, messages: { ...ERROR_MESSAGES, forbidden: ERROR_MESSAGES.access_forbidden, internal_error: fallback }, reasons: NOT_EVALUABLE_REASON_MESSAGES });
  return { tone: 'error', text: errorNotice.text, traceId: errorNotice.traceId, sessionEnded: errorNotice.action === 'login', background: background && !resourceUnavailable(error), accessDenied: errorNotice.kind === 'forbidden' };
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
  const { login, captureSessionEnd } = useAuth();
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
  const revokedPosition = useRef<string | null>(null);
  const accessGeneration = useRef(0);
  const isPositionAccessible = useCallback(() => currentIdRef.current === id && revokedPosition.current !== id, [id]);
  const isPositionRevoked = useCallback(() => currentIdRef.current === id && revokedPosition.current === id, [id]);

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
      const isCurrent = () => generation === positionGeneration.current && currentIdRef.current === id && !signal.aborted;
      return getPosition(id, signal).then(
      (value) => view.apply(() => {
        if (isCurrent()) {
          setPosition(loaded(id, value));
          setNotFound(null);
          if (revokedPosition.current === id) {
            revokedPosition.current = null;
            marketRead.current?.invalidate();
            candlesRead.current?.invalidate();
            evaluationRead.current?.invalidate();
          }
        }
      }, isCurrent),
      (error: unknown) => {
        if (!isCurrent()) {
          return;
        }

        const notice = toErrorNotice(error, 'Не удалось загрузить позицию. Повторите попытку позже.', view.kind === 'live');
        view.fail(error, () => {
          if (isCurrent()) setPosition((current) => failed(id, notice, current));
        }, () => {
          revokedPosition.current = id;
          accessGeneration.current += 1;
          positionGeneration.current += 1;
          marketGeneration.current += 1;
          candlesGeneration.current += 1;
          evaluationGeneration.current += 1;
          setNotFound(isNotFound(error) ? id : null);
          setPosition(failed(id, notice, null));
          setMarket(null);
          setCandles(null);
          setEvaluation(null);
        }, isCurrent);
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
      const isCurrent = () => generation === marketGeneration.current && currentIdRef.current === id && revokedPosition.current !== id && !signal.aborted;
      return getPositionMarket(id, signal).then(
      (value) => view.apply(() => {
        if (isCurrent()) {
          setMarket(loaded(id, value));
        }
      }, isCurrent),
      (error: unknown) => {
        if (isCurrent()) {
          view.fail(error, () => {
            if (isCurrent()) setMarket((current) => failed(id, toErrorNotice(error, 'Не удалось загрузить рыночный контекст. Повторите попытку позже.', view.kind === 'live'), current));
          }, () => {
            marketGeneration.current += 1;
            setMarket(failed(id, toErrorNotice(error, 'Не удалось загрузить рыночный контекст. Повторите попытку позже.'), null));
          }, isCurrent);
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
      const isCurrent = () => generation === candlesGeneration.current && currentIdRef.current === id && revokedPosition.current !== id && !signal.aborted;
      return getPositionCandles(id, interval, signal).then(
      (value) => view.apply(() => {
        if (isCurrent()) {
          setCandles(loaded(key, value));
        }
      }, isCurrent),
      (error: unknown) => {
        if (isCurrent()) {
          view.fail(error, () => {
            if (isCurrent()) setCandles((current) => failed(key, toErrorNotice(error, 'Не удалось загрузить свечи. Повторите попытку позже.', view.kind === 'live'), current));
          }, () => {
            candlesGeneration.current += 1;
            setCandles(failed(key, toErrorNotice(error, 'Не удалось загрузить свечи. Повторите попытку позже.'), null));
          }, isCurrent);
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
      const isCurrent = () => generation === evaluationGeneration.current && currentIdRef.current === id && revokedPosition.current !== id && !signal.aborted;
      return getPositionEvaluation(id, signal).then(
      (value) => view.apply(() => {
        if (isCurrent()) {
          setEvaluation({ ...loaded(id, value), appliedAt: Date.now() });
        }
      }, isCurrent),
      (error: unknown) => {
        if (isCurrent()) {
          view.fail(error, () => {
            if (isCurrent()) setEvaluation((current) => ({
              ...failed(id, toErrorNotice(error, 'Не удалось загрузить оценку. Повторите попытку позже.', view.kind === 'live'), current),
              appliedAt: current?.key === id ? current.appliedAt : null,
            }));
          }, () => {
            evaluationGeneration.current += 1;
            setEvaluation({ ...failed(id, toErrorNotice(error, 'Не удалось загрузить оценку. Повторите попытку позже.'), null), appliedAt: null });
          }, isCurrent);
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
    const endSession = captureSessionEnd();
    const requestGeneration = accessGeneration.current;
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
      if (currentIdRef.current === positionId && revokedPosition.current !== positionId && accessGeneration.current === requestGeneration) {
        evaluationGeneration.current += 1;
        setEvaluation({ ...loaded(positionId, value), appliedAt: Date.now() });
      }

      notice = { tone: 'success', text: 'Оценка выполнена.' };
    } catch (error) {
      notice = toErrorNotice(error, 'Не удалось выполнить оценку. Повторите попытку позже.');
      if (notice.sessionEnded) endSession(notice.traceId);
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
        <StatusMessage kind="empty" text="Позиция не найдена или недоступна для текущего пользователя." traceId={currentPosition?.error?.traceId} />
      </section>
    );
  }

  if (currentPosition?.error?.accessDenied) {
    return (
      <section className="page position-page" aria-labelledby={titleId}>
        {backLink}
        <h1 id={titleId}>Позиция недоступна</h1>
        <NoticeMessage notice={currentPosition.error} onLogin={login} onRetry={() => positionRead.current?.refresh()} />
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
        <StatusMessage kind="loading" text="Загружаем позицию…" />
      )}
      {currentPosition?.error && (
        <NoticeMessage notice={currentPosition.error} onLogin={login} onRetry={() => positionRead.current?.refresh()} />
      )}
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
          <StatusMessage kind="loading" text="Загружаем свечи…" />
        )}
        {currentCandles?.error && <NoticeMessage notice={currentCandles.error} onLogin={login} onRetry={() => candlesRead.current?.refresh()} />}
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
        <StatusMessage kind="loading" text="Загружаем рыночный контекст…" />
      )}
      {currentMarket?.error && (
        <section className="panel" aria-label="Рыночный контекст">
          <NoticeMessage notice={currentMarket.error} onLogin={login} onRetry={() => marketRead.current?.refresh()} />
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
          <StatusMessage kind="loading" text="Выполняем оценку позиции…" />
        )}
        {evaluateState?.notice && <NoticeMessage notice={evaluateState.notice} onLogin={login} />}
        {currentEvaluation === null && (
          <StatusMessage kind="loading" text="Загружаем оценку…" />
        )}
        {currentEvaluation?.error && <NoticeMessage notice={currentEvaluation.error} onLogin={login} onRetry={() => evaluationRead.current?.refresh()} />}
        {currentEvaluation !== null && currentEvaluation.appliedAt !== null && evaluationValue === null && (
          <StatusMessage kind="empty" text="Оценка ещё не выполнялась. Запустите её кнопкой «Переоценить»." />
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
          captureSessionEnd={captureSessionEnd}
          isPositionAccessible={isPositionAccessible}
          isPositionRevoked={isPositionRevoked}
        />
      )}
    </section>
  );
}

function NoticeMessage({ notice, onLogin, onRetry }: { notice: Notice; onLogin: () => void; onRetry?: () => void }) {
  const actions = notice.sessionEnded && (
    <button type="button" className="button button-secondary" onClick={onLogin}>
      Войти снова
    </button>
  ) || (onRetry && <button type="button" className="button button-secondary" onClick={onRetry}>Повторить</button>);
  return <StatusMessage kind={notice.background ? 'warning' : notice.tone} text={notice.text} traceId={notice.traceId} actions={actions} />;
}
