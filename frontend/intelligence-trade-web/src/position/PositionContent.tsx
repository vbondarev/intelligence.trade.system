import { presentError } from '../errors/errorPresentation';
import { StatusMessage } from '../ui/StatusMessage';
import { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { createPortal } from 'react-dom';
import { Link } from 'react-router-dom';
import type { SessionReadContext } from '../auth/authTypes';
import type { PositionEvaluationRequests } from './usePositionEvaluationRequests';
import { useAuth } from '../auth/authContext';
import { resourceUnavailable, type ScreenReader } from '../realtime/autoUpdates';
import { useAutoUpdates, useAutoUpdateState } from '../realtime/autoUpdatesContext';
import { useRealtimeInvalidation } from '../realtime/useRealtimeInvalidation';
import { SIDE_LABELS, formatAssetAmount, formatNumber, formatSignedAssetAmount, pnlClassName } from '../portfolio/portfolioFormatting';
import { PositionChart } from './PositionChart';
import { PositionChartData } from './PositionChartData';
import { toChartCandles, toPriceLevels, type ChartCandle } from './positionChartAdapter';
import { PositionDetails } from './PositionDetails';
import { PositionEvaluation } from './PositionEvaluation';
import { PositionMarketContext } from './PositionMarketContext';
import { PositionRecommendation } from './PositionRecommendation';
import { PositionTimeline } from './PositionTimeline';
import {
  PositionApiError,
  evaluatePosition,
  getPosition,
  getPositionCandles,
  getPositionEvaluation,
  getPositionMarket,
} from './positionApi';
import {
  CHART_INTERVALS,
  CHART_INTERVAL_LABELS,
  NOT_EVALUABLE_REASON_MESSAGES,
} from './positionFormatting';
import { POSITION_SEARCH_PATH, resolveReturnPath } from './positionNavigation';
import { useValidityClock } from './useValidityClock';
import type {
  ChartInterval,
  PositionCandles,
  PositionDetails as Position,
  PositionEvaluation as Evaluation,
  PositionMarket,
} from './positionTypes';

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

// UI ветвится только по HTTP status, machine-readable code и reason: title/detail не показываются.
function toErrorNotice(error: unknown, fallback: string, background = false): Notice {
  const errorNotice = presentError(error, { fallback: fallback, messages: { ...ERROR_MESSAGES, forbidden: ERROR_MESSAGES.access_forbidden, internal_error: fallback }, reasons: NOT_EVALUABLE_REASON_MESSAGES });
  return { tone: 'error', text: errorNotice.text, traceId: errorNotice.traceId, sessionEnded: errorNotice.action === 'login', background: background && !resourceUnavailable(error), accessDenied: errorNotice.kind === 'forbidden' };
}

function isNotFound(error: unknown): boolean {
  return error instanceof PositionApiError && error.status === 404;
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
interface PositionContentProps {
  id: string;
  interval: ChartInterval;
  onIntervalChange: (interval: ChartInterval) => void;
  requests: PositionEvaluationRequests;
  routeState?: unknown;
  preview?: { meta: HTMLSpanElement | null; onAccessLost: () => void };
}

export function PositionPreview({ id, requests, onAccessLost, previewMeta }: Pick<PositionContentProps, 'id' | 'requests'> & {
  onAccessLost: () => void;
  previewMeta: HTMLSpanElement | null;
}) {
  const [interval, setInterval] = useState<ChartInterval>('1h');
  return <PositionContent id={id} requests={requests} interval={interval} onIntervalChange={setInterval} preview={{ meta: previewMeta, onAccessLost }} />;
}

export function PositionContent({ id, interval, onIntervalChange, requests, routeState, preview: previewPresentation }: PositionContentProps) {
  const { login, captureSessionRead } = useAuth();
  const updates = useAutoUpdates();
  const { pausedAt } = useAutoUpdateState();
  const onAccessLost = previewPresentation?.onAccessLost;
  const preview = previewPresentation !== undefined;
  const SectionHeading = preview ? 'h3' : 'h2';
  const candlesKey = `${id}|${interval}`;
  const titleId = useId();
  const chartTitleId = useId();
  const evaluationTitleId = useId();

  const [hasOpenedDetails, setHasOpenedDetails] = useState(false);
  const [position, setPosition] = useState<Load<Position> | null>(null);
  const [notFound, setNotFound] = useState<string | null>(null);
  const [market, setMarket] = useState<Load<PositionMarket> | null>(null);
  const [candles, setCandles] = useState<Load<PositionCandles> | null>(null);
  const [evaluation, setEvaluation] = useState<EvaluationLoad | null>(null);
  const [evaluateNotice, setEvaluateNotice] = useState<(Notice & { positionId: string }) | null>(null);
  const [confirmingPosition, setConfirmingPosition] = useState<string | null>(null);
  const confirming = confirmingPosition === id;
  const running = useSyncExternalStore(requests.subscribe, () => requests.isRunning(id));

  const positionRead = useRef<ScreenReader | null>(null);
  const marketRead = useRef<ScreenReader | null>(null);
  const candlesRead = useRef<ScreenReader | null>(null);
  const evaluationRead = useRef<ScreenReader | null>(null);
  const positionGeneration = useRef(0);
  const marketGeneration = useRef(0);
  const candlesGeneration = useRef(0);
  const evaluationGeneration = useRef(0);
  const opening = useRef(Symbol('position-opening'));
  const currentIdRef = useRef(id);
  const revokedPosition = useRef<string | null>(null);
  const accessGeneration = useRef(0);
  const isPositionAccessible = useCallback(() => currentIdRef.current === id && revokedPosition.current !== id, [id]);
  const isPositionRevoked = useCallback(() => currentIdRef.current === id && revokedPosition.current === id, [id]);

  const evaluationRevision = useRef(0);
  const appliedEvaluationRevision = useRef<number | null>(null);
  const confirmation = useRef<{ revision: number; session: SessionReadContext } | null>(null);

  const confirmEvaluation = useCallback(() => {
    const pending = confirmation.current;
    if (pending === null) return;
    if (revokedPosition.current === id || !pending.session.isCurrent() || appliedEvaluationRevision.current === pending.revision) {
      confirmation.current = null;
      setConfirmingPosition(null);
      return;
    }
    if (evaluationRead.current?.refreshAfterWrite()) confirmation.current = null;
  }, [id]);

  useLayoutEffect(() => {
    currentIdRef.current = id;
    opening.current = Symbol('position-opening');
    appliedEvaluationRevision.current = null;
    const stopCompletion = requests.onCompletion((completion) => {
      if (completion.positionId !== id || !completion.session.isCurrent() || revokedPosition.current === id) return;
      const fromHere = completion.opening === opening.current;
      if (fromHere && completion.accessGeneration !== accessGeneration.current) return;

      if (!completion.ok) {
        if (fromHere) setEvaluateNotice({
          ...toErrorNotice(completion.error, 'Не удалось выполнить оценку. Повторите попытку позже.'),
          positionId: id,
        });
        return;
      }

      // Граница записи проверяется повторно перед применением подготовленного результата resume.
      const revision = ++evaluationRevision.current;
      if (fromHere) {
        appliedEvaluationRevision.current = revision;
        setEvaluation({ ...loaded(id, completion.value), appliedAt: Date.now() });
        setEvaluateNotice({ tone: 'success', text: 'Оценка выполнена.', positionId: id });
      } else {
        confirmation.current = { revision, session: completion.session };
        setConfirmingPosition(id);
        confirmEvaluation();
      }
    });
    let active = true;
    let queued = false;
    const stopUpdates = updates.subscribe(() => {
      if (confirmation.current === null || queued || updates.getSnapshot().mode === 'resuming') return;
      queued = true;
      queueMicrotask(() => {
        queued = false;
        if (active) confirmEvaluation();
      });
    });
    return () => {
      active = false;
      confirmation.current = null;
      stopUpdates();
      stopCompletion();
    };
  }, [id, requests, updates, confirmEvaluation]);

  const [noticePosition, setNoticePosition] = useState(id);
  if (noticePosition !== id) {
    setNoticePosition(id);
    setEvaluateNotice(null);
    setConfirmingPosition(null);
  }

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
          confirmation.current = null;
          setConfirmingPosition(null);
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
          onAccessLost?.();
        }, isCurrent);
      },
      );
    });
    positionRead.current = scheduler;
    scheduler.refresh();
    return () => { positionRead.current = null; scheduler.dispose(); };
  }, [id, updates, onAccessLost]);

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
      const revision = evaluationRevision.current;
      const hasAccess = () => generation === evaluationGeneration.current && currentIdRef.current === id && revokedPosition.current !== id && !signal.aborted;
      const isCurrent = () => hasAccess() && revision === evaluationRevision.current;
      return getPositionEvaluation(id, signal).then(
        (value) => view.apply(() => {
          if (!isCurrent()) return;
          appliedEvaluationRevision.current = revision;
          setConfirmingPosition(null);
          setEvaluation({ ...loaded(id, value), appliedAt: Date.now() });
        }, isCurrent),
        (error: unknown) => {
          const accessFailure = resourceUnavailable(error) || (error instanceof PositionApiError && error.status === 401);
          const eligible = accessFailure ? hasAccess : isCurrent;
          if (!eligible()) return;
          const notice = toErrorNotice(error, 'Не удалось загрузить оценку. Повторите попытку позже.', view.kind === 'live');
          view.fail(error, () => {
            if (!eligible()) return;
            const previouslyApplied = appliedEvaluationRevision.current === revision;
            setConfirmingPosition(null);
            setEvaluation((current) => ({
              ...failed(id, notice, current),
              appliedAt: previouslyApplied && current?.key === id ? current.appliedAt : null,
            }));
          }, () => {
            evaluationGeneration.current += 1;
            confirmation.current = null;
            setConfirmingPosition(null);
            setEvaluation({ ...failed(id, notice, null), appliedAt: null });
          }, eligible);
        },
      );
    });
    evaluationRead.current = scheduler;
    if (confirmation.current) confirmEvaluation();
    else scheduler.refresh();
    return () => { evaluationRead.current = null; scheduler.dispose(); };
  }, [id, updates, confirmEvaluation]);

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
  const evaluationKnownEmpty = currentEvaluation?.appliedAt != null && evaluationValue === null;
  const evaluateLabel = evaluationValue !== null ? 'Переоценить' : evaluationKnownEmpty ? 'Оценить позицию' : 'Выполнить оценку';
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

  function handleEvaluate() {
    if (requests.isRunning(id) || confirming || revokedPosition.current === id) return;
    const session = captureSessionRead();
    updates.cancelResume();
    setEvaluateNotice(null);
    requests.start({ positionId: id, opening: opening.current, accessGeneration: accessGeneration.current, session }, async () => {
      try {
        return await evaluatePosition(id);
      } catch (error) {
        const notice = toErrorNotice(error, 'Не удалось выполнить оценку. Повторите попытку позже.');
        if (notice.sessionEnded) session.endSession(notice.traceId);
        throw error;
      }
    });
  }

  const returnPath = resolveReturnPath(routeState, positionValue?.exchangeAccountId ?? null);
  const backLink = !preview && (
    <Link to={returnPath} className="back-link">
      {returnPath.split('?')[0] === POSITION_SEARCH_PATH ? '← К результатам поиска' : '← К списку позиций'}
    </Link>
  );

  const className = preview ? 'position-preview-content' : 'page position-page';

  if (notFound === id) {
    return (
      <section className={className} aria-labelledby={preview ? undefined : titleId}>
        {backLink}
        {!preview && <h1 id={titleId}>Позиция недоступна</h1>}
        <StatusMessage kind="empty" text="Позиция не найдена или недоступна для текущего пользователя." traceId={currentPosition?.error?.traceId} />
      </section>
    );
  }

  if (currentPosition?.error?.accessDenied) {
    return (
      <section className={className} aria-labelledby={preview ? undefined : titleId}>
        {backLink}
        {!preview && <h1 id={titleId}>Позиция недоступна</h1>}
        <NoticeMessage notice={currentPosition.error} onLogin={login} onRetry={() => positionRead.current?.refresh()} />
      </section>
    );
  }

  const symbol = positionValue?.symbol ?? marketValue?.symbol ?? null;
  const title =
    positionValue === null
      ? (symbol ?? 'Позиция')
      : `${positionValue.symbol} · ${formatNumber(positionValue.size)} ${SIDE_LABELS[positionValue.side]}`;

  const evaluationButton = (
    <button
      type="button"
      className="button"
      disabled={running || confirming}
      onClick={handleEvaluate}
    >
      {running ? 'Оценка выполняется…' : confirming ? 'Подтверждаем оценку…' : evaluateLabel}
    </button>
  );
  const evaluationPanel = (
    <section className="panel evaluation-panel" aria-labelledby={evaluationTitleId}>
      <header className="evaluation-header">
        <SectionHeading id={evaluationTitleId} className="section-title">
          Оценка и рекомендация
        </SectionHeading>
        {!preview && evaluationButton}
      </header>
      {running && (
        <StatusMessage kind="loading" text="Выполняем оценку позиции…" />
      )}
        {evaluateNotice?.positionId === id && <NoticeMessage notice={evaluateNotice} onLogin={login} />}
      {(currentEvaluation === null || confirming) && (
        <StatusMessage kind="loading" text="Загружаем оценку…" />
      )}
      {currentEvaluation?.error && !confirming && <NoticeMessage notice={currentEvaluation.error} onLogin={login} onRetry={() => evaluationRead.current?.refresh()} />}
        {evaluationKnownEmpty && !running && !confirming && (
        <StatusMessage kind="empty" text="Оценка ещё не выполнялась. Запустите её кнопкой «Оценить позицию»." />
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
      {preview && evaluationButton}
    </section>
  );

  const details = (
    <>
      {positionValue !== null && <PositionDetails position={positionValue} heading={SectionHeading} />}

      <section className="panel chart-panel" aria-labelledby={chartTitleId}>
        <header className="chart-header">
          <SectionHeading id={chartTitleId} className="section-title">
            График
          </SectionHeading>
          <div className="segmented" role="group" aria-label="Интервал графика">
            {CHART_INTERVALS.map((option) => (
              <button
                key={option}
                type="button"
                className="segmented-option"
                aria-pressed={option === interval}
                onClick={() => onIntervalChange(option)}
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
        {(!preview || hasOpenedDetails) && (
          <PositionChart
            candles={chartCandles}
            levels={levels}
            dataKey={candlesKey}
            label={`Свечной график ${symbol ?? 'позиции'}, интервал ${CHART_INTERVAL_LABELS[interval]}`}
          />
        )}
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
      {marketValue !== null && <PositionMarketContext market={marketValue} heading={SectionHeading} />}
    </>
  );

  const meta = previewPresentation?.meta;
  return (
    <section className={className} aria-labelledby={preview ? undefined : titleId}>
      {backLink}
      {!preview && <h1 id={titleId}>{title}</h1>}

      {currentPosition === null && (
        <StatusMessage kind="loading" text="Загружаем позицию…" />
      )}
      {currentPosition?.error && (
        <NoticeMessage notice={currentPosition.error} onLogin={login} onRetry={() => positionRead.current?.refresh()} />
      )}
      {preview && positionValue && (
        <>
          {meta && createPortal(
            <>{SIDE_LABELS[positionValue.side]} · {formatAssetAmount(positionValue.positionValue, positionValue.settlementAsset)}</>,
            meta,
          )}
          <dl className="overview-preview-metrics" aria-label="Краткие параметры позиции">
            <div>
              <dt>PnL</dt>
              <dd className={pnlClassName(positionValue.unrealizedPnl)}>
                {formatSignedAssetAmount(positionValue.unrealizedPnl, positionValue.settlementAsset)}
              </dd>
            </div>
            <div>
              <dt>Вход</dt>
              <dd>{formatNumber(positionValue.averageEntryPrice)}</dd>
            </div>
            <div>
              <dt>Mark позиции</dt>
              <dd>{formatNumber(positionValue.markPrice)}</dd>
            </div>
          </dl>
        </>
      )}
      {preview ? (
        <>
          {evaluationPanel}
          <details className="overview-disclosure" onToggle={(event) => {
            if (event.currentTarget.open && !hasOpenedDetails) setHasOpenedDetails(true);
          }}>
            <summary>Рыночные данные и параметры</summary>
            <div className="position-preview-details">{details}</div>
          </details>
        </>
      ) : (
        <>
          {details}
          {evaluationPanel}
        </>
      )}

      {positionValue !== null && (
        <PositionTimeline
          key={id}
          positionId={id}
          settlementAsset={positionValue.settlementAsset}
          onLogin={login}
          captureSessionRead={captureSessionRead}
          isPositionAccessible={isPositionAccessible}
          isPositionRevoked={isPositionRevoked}
          heading={SectionHeading}
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
