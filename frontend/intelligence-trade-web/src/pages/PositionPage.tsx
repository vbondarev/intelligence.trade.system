import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom';
import { useAuth } from '../auth/authContext';
import { SIDE_LABELS, formatNumber } from '../portfolio/portfolioFormatting';
import { PositionChart } from '../position/PositionChart';
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
import { resolveReturnPath } from '../position/positionNavigation';
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
type Load<T> = { key: string; value: T; error: null } | { key: string; value: null; error: Notice };

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

function failed<T>(key: string, error: Notice): Load<T> {
  return { key, value: null, error };
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
  const [evaluation, setEvaluation] = useState<Load<Evaluation | null> | null>(null);
  const [evaluateStates, setEvaluateStates] = useState<Readonly<Record<string, EvaluateState>>>({});

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
    const controller = new AbortController();
    getPosition(id, controller.signal).then(
      (value) => {
        if (!controller.signal.aborted) {
          setPosition(loaded(id, value));
          setNotFound(null);
        }
      },
      (error: unknown) => {
        if (controller.signal.aborted) {
          return;
        }

        if (isNotFound(error)) {
          setNotFound(id);
        }

        setPosition(failed(id, toErrorNotice(error, 'Не удалось загрузить позицию. Повторите попытку позже.')));
      },
    );
    return () => controller.abort();
  }, [id]);

  useEffect(() => {
    const controller = new AbortController();
    getPositionMarket(id, controller.signal).then(
      (value) => {
        if (!controller.signal.aborted) {
          setMarket(loaded(id, value));
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setMarket(failed(id, toErrorNotice(error, 'Не удалось загрузить рыночный контекст. Повторите попытку позже.')));
        }
      },
    );
    return () => controller.abort();
  }, [id]);

  useEffect(() => {
    const controller = new AbortController();
    const key = `${id}|${interval}`;
    getPositionCandles(id, interval, controller.signal).then(
      (value) => {
        if (!controller.signal.aborted) {
          setCandles(loaded(key, value));
        }
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          setCandles(failed(key, toErrorNotice(error, 'Не удалось загрузить свечи. Повторите попытку позже.')));
        }
      },
    );
    return () => controller.abort();
  }, [id, interval]);

  useEffect(() => {
    const controller = new AbortController();
    const generation = ++evaluationGeneration.current;
    const isCurrent = () => generation === evaluationGeneration.current && !controller.signal.aborted;
    getPositionEvaluation(id, controller.signal).then(
      (value) => {
        if (isCurrent()) {
          setEvaluation(loaded(id, value));
        }
      },
      (error: unknown) => {
        if (isCurrent()) {
          setEvaluation(failed(id, toErrorNotice(error, 'Не удалось загрузить оценку. Повторите попытку позже.')));
        }
      },
    );
    return () => controller.abort();
  }, [id]);

  const currentPosition = position?.key === id ? position : null;
  const currentMarket = market?.key === id ? market : null;
  const currentCandles = candles?.key === candlesKey ? candles : null;
  const currentEvaluation = evaluation?.key === id ? evaluation : null;
  const evaluationValue = currentEvaluation?.value ?? null;
  const evaluateState = evaluateStates[id];
  const now = useValidityClock([
    evaluationValue?.assessment.validUntil,
    evaluationValue?.recommendation?.validUntil,
  ]);

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

    const update = (state: EvaluateState) => setEvaluateStates((current) => ({ ...current, [positionId]: state }));
    evaluateInFlight.current.add(positionId);
    update({ running: true, notice: null });
    let notice: Notice;
    try {
      const value = await evaluatePosition(positionId);
      if (currentIdRef.current === positionId) {
        evaluationGeneration.current += 1;
        setEvaluation(loaded(positionId, value));
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
      ← К списку позиций
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
