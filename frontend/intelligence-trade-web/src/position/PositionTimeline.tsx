import { useCallback, useEffect, useId, useRef, useState, type MouseEvent } from 'react';
import { explicitPresentation, type ReadPresentation, type ScreenReader } from '../realtime/autoUpdates';
import { useAutoUpdates } from '../realtime/autoUpdatesContext';
import { useRealtimeInvalidation } from '../realtime/useRealtimeInvalidation';
import { PositionApiError, TIMELINE_ITEM_TYPES, getPositionTimeline } from './positionApi';
import { PositionTimelineItem } from './PositionTimelineItem';
import { TIMELINE_FILTER_LABELS, timelineItemKey } from './positionTimelineFormatting';
import type { PositionTimelineItem as TimelineItem, PositionTimelineItemType, PositionTimelinePage } from './positionTypes';

const SESSION_ENDED = 'Сессия завершена. Войдите снова.';
const SERVICE_UNAVAILABLE = 'Сервис временно недоступен. Повторите попытку позже.';

type LoadPhase = 'initial' | 'more' | 'refresh';
type LoadSource = 'default' | 'manual-refresh';

interface ActiveLoad {
  controller: AbortController;
  generation: number;
  source: LoadSource;
}

interface LoadOptions {
  schedulerSignal?: AbortSignal;
  view?: ReadPresentation;
  source?: LoadSource;
}

const PHASE_FALLBACKS: Record<LoadPhase, string> = {
  initial: 'Не удалось загрузить историю позиции. Повторите попытку позже.',
  more: 'Не удалось загрузить продолжение истории. Повторите попытку позже.',
  refresh: 'Не удалось обновить историю позиции. Повторите попытку позже.',
};

interface TimelineNotice {
  text: string;
  traceId: string | null;
  sessionEnded: boolean;
  canRetry: boolean;
}

interface TimelineState {
  isOpen: boolean;
  selectedTypes: readonly PositionTimelineItemType[];
  items: readonly TimelineItem[];
  nextCursor: string | null;
  hasMore: boolean;
  hasLoaded: boolean;
  pending: { phase: LoadPhase; source: LoadSource | 'recovery'; generation: number } | null;
  error: { phase: LoadPhase; notice: TimelineNotice } | null;
}

const INITIAL_STATE: TimelineState = {
  isOpen: false,
  selectedTypes: TIMELINE_ITEM_TYPES,
  items: [],
  nextCursor: null,
  hasMore: false,
  hasLoaded: false,
  pending: null,
  error: null,
};

interface PositionTimelineProps {
  positionId: string;
  settlementAsset: string;
  onLogin: () => void;
}

// UI ветвится только по HTTP status и machine-readable code: title/detail не показываются.
function toNotice(error: unknown, phase: LoadPhase): TimelineNotice {
  const notice = (text: string, traceId: string | null = null): TimelineNotice => ({
    text,
    traceId,
    sessionEnded: false,
    canRetry: true,
  });

  if (!(error instanceof PositionApiError)) {
    return notice(SERVICE_UNAVAILABLE);
  }

  if (error.status === 401 || error.code === 'authentication_required') {
    return { text: SESSION_ENDED, traceId: null, sessionEnded: true, canRetry: false };
  }

  if (error.status === 404 || error.code === 'resource_not_found') {
    return { ...notice('История позиции недоступна.', error.traceId), canRetry: false };
  }

  if (error.status === 400) {
    return notice('Запрос истории отклонён: некорректные параметры запроса.', error.traceId);
  }

  if (error.status === 403) {
    return { ...notice('Недостаточно прав для просмотра истории позиции.', error.traceId), canRetry: false };
  }

  if (error.status === 503) {
    return notice(SERVICE_UNAVAILABLE, error.traceId);
  }

  if (error.status >= 200 && error.status < 300) {
    return notice('Получены некорректные данные истории позиции.', error.traceId);
  }

  return notice(PHASE_FALLBACKS[phase], error.traceId);
}

function appendUnique(current: readonly TimelineItem[], page: readonly TimelineItem[]): TimelineItem[] {
  const known = new Set(current.map(timelineItemKey));
  const appended: TimelineItem[] = [];
  for (const item of page) {
    const key = timelineItemKey(item);
    if (!known.has(key)) {
      known.add(key);
      appended.push(item);
    }
  }

  return [...current, ...appended];
}

/**
 * История событий позиции. Блок изначально свёрнут: первая страница запрашивается только при
 * первом раскрытии, продолжение — командой «Показать ещё», актуальная история — командой
 * «Обновить». Каждое чтение привязано к поколению загрузки: смена фильтра, обновление и
 * unmount отменяют прежний запрос, а его поздний ответ не применяется. Polling, sync и оценку
 * блок не запускает.
 */
export function PositionTimeline({ positionId, settlementAsset, onLogin }: PositionTimelineProps) {
  const updates = useAutoUpdates();
  const titleId = useId();
  const contentId = useId();
  const filterHintId = useId();
  const [state, setState] = useState<TimelineState>(INITIAL_STATE);
  const [manualLoadStatus, setManualLoadStatus] = useState('');
  // После завершения сессии доступ восстанавливает только повторный вход: ни одна команда
  // блока больше не отправляет timeline-запрос.
  const sessionEnded = state.error?.notice.sessionEnded === true;

  const generation = useRef(0);
  const isOpenRef = useRef(false);
  const invalidationVersion = useRef(0);
  const appliedInvalidationVersion = useRef(0);
  const activeLoad = useRef<ActiveLoad | null>(null);
  const stateRef = useRef(state);
  const realtimeRead = useRef<ScreenReader | null>(null);
  const refreshButton = useRef<HTMLButtonElement>(null);
  useEffect(() => { stateRef.current = state; }, [state]);

  const hasCurrentManualLoad = useCallback(() =>
    activeLoad.current?.source === 'manual-refresh' && activeLoad.current.generation === generation.current, []);

  useEffect(
    () => () => {
      generation.current += 1;
      activeLoad.current?.controller.abort();
    },
    [],
  );

  const cancelActive = useCallback(() => {
    generation.current += 1;
    activeLoad.current?.controller.abort();
    activeLoad.current = null;
  }, []);

  // Ответ применяется, только если запрос не отменён и его поколение осталось текущим:
  // одной отмены недостаточно для защиты от уже начатого разбора ответа.
  const load = useCallback((
    phase: LoadPhase,
    types: readonly PositionTimelineItemType[],
    cursor: string | null,
    { schedulerSignal, view = explicitPresentation, source = 'default' }: LoadOptions = {},
  ): Promise<void> => {
    if (view.kind === 'silent' || view.kind === 'resume') {
      const version = generation.current;
      const isCurrent = () => isOpenRef.current && generation.current === version && !schedulerSignal?.aborted;
      return getPositionTimeline(positionId, { types, cursor }, schedulerSignal).then(
        (page) => view.apply(() => {
          if (!isCurrent()) return;
          cancelActive();
          appliedInvalidationVersion.current = invalidationVersion.current;
          setManualLoadStatus('');
          setState((current) => applyPage(current, phase, cursor, page));
        }),
        (error: unknown) => {
          if (!isCurrent()) return;
          view.fail(error, () => {
            if (!isCurrent()) return;
            cancelActive();
            setManualLoadStatus('');
            setState((current) => ({ ...current, pending: null, error: { phase, notice: toNotice(error, phase) } }));
          });
        },
      );
    }

    if (view.kind === 'explicit' && !schedulerSignal) {
      realtimeRead.current?.supersedeExplicitRead();
    }
    cancelActive();
    const requestGeneration = generation.current;
    const requestInvalidationVersion = invalidationVersion.current;
    const requestController = new AbortController();
    const request = { controller: requestController, generation: requestGeneration, source };
    activeLoad.current = request;
    const isCurrent = () => requestGeneration === generation.current && !requestController.signal.aborted;
    const readDeferredInvalidation = () => {
      if (source === 'manual-refresh' && isCurrent() && isOpenRef.current
        && invalidationVersion.current > requestInvalidationVersion) {
        realtimeRead.current?.invalidate();
      }
    };

    if (view.kind === 'explicit') {
      setState((current) => ({
        ...current,
        pending: { phase, source, generation: requestGeneration },
        error: null,
        // Cursor прежнего поколения не комбинируется со страницами нового.
        ...(phase === 'more' ? {} : { nextCursor: null, hasMore: false }),
      }));
    } else {
      // Recovery сохраняет видимый статус заменённого запроса и использует конечную анимацию.
      setState((current) => current.pending?.source === 'manual-refresh' || current.pending?.source === 'recovery'
        ? { ...current, pending: { phase, source: 'recovery', generation: requestGeneration } }
        : current);
    }

    const abortRequest = () => {
      requestController.abort();
      if (activeLoad.current !== request || requestGeneration !== generation.current) return;
      activeLoad.current = null;
      if (view.kind === 'live') {
        setManualLoadStatus('');
        setState((current) => current.pending?.generation === requestGeneration
          ? { ...current, pending: null }
          : current);
      }
    };
    schedulerSignal?.addEventListener('abort', abortRequest, { once: true });
    if (schedulerSignal?.aborted) abortRequest();

    return getPositionTimeline(positionId, { types, cursor }, requestController.signal).then(
      (page) => {
        if (!isCurrent()) {
          return;
        }

        activeLoad.current = null;
        view.apply(() => {
          setManualLoadStatus('');
          if (phase !== 'more') {
            appliedInvalidationVersion.current = Math.max(appliedInvalidationVersion.current, requestInvalidationVersion);
          }
          setState((current) => applyPage(current, phase, cursor, page));
        });
        readDeferredInvalidation();
      },
      (error: unknown) => {
        if (!isCurrent()) {
          return;
        }

        activeLoad.current = null;
        const notice = toNotice(error, phase);
        view.fail(error, () => {
          setManualLoadStatus('');
          setState((current) => ({ ...current, pending: null, error: { phase, notice } }));
        });
        if (!notice.sessionEnded) readDeferredInvalidation();
      },
    ).finally(() => schedulerSignal?.removeEventListener('abort', abortRequest));
  }, [cancelActive, positionId]);

  useEffect(() => {
    const scheduler = updates.createReader((signal, view) => {
      const current = stateRef.current;
      if (!isOpenRef.current || current.error?.notice.sessionEnded === true) return Promise.resolve();
      if (view.kind === 'live' && hasCurrentManualLoad()) return Promise.resolve();
      return load(current.hasLoaded ? 'refresh' : 'initial', current.selectedTypes, null, { schedulerSignal: signal, view });
    });
    realtimeRead.current = scheduler;
    return () => { realtimeRead.current = null; scheduler.dispose(); };
  }, [hasCurrentManualLoad, load, updates]);

  useRealtimeInvalidation((event) => {
    if (sessionEnded || !(event.type === 'recovery'
      || ((event.type === 'position.updated' || event.type === 'evaluation.updated') && event.positionId === positionId))) return;
    invalidationVersion.current += 1;
    if (!isOpenRef.current) return;
    if (event.type === 'recovery') {
      if (updates.isLive()) generation.current += 1;
      realtimeRead.current?.recover();
    } else if (!updates.isLive() || !hasCurrentManualLoad()) {
      realtimeRead.current?.invalidate();
    }
  });

  const handleToggleOpen = () => {
    updates.cancelResume();
    if (isOpenRef.current) {
      isOpenRef.current = false;
      realtimeRead.current?.supersedeExplicitRead();
      cancelActive();
      setManualLoadStatus('');
      realtimeRead.current?.recover();
      setState((current) => ({ ...current, isOpen: false, pending: null }));
      return;
    }

    isOpenRef.current = true;
    setState((current) => ({ ...current, isOpen: true }));
    if ((!state.hasLoaded || invalidationVersion.current > appliedInvalidationVersion.current)
      && state.error?.notice.canRetry !== false) {
      setManualLoadStatus(state.hasLoaded ? 'Обновляем историю…' : 'Загружаем историю…');
      realtimeRead.current?.refresh();
    }
  };

  const handleToggleType = (type: PositionTimelineItemType) => {
    const selected = state.selectedTypes.includes(type);
    if (sessionEnded || (selected && state.selectedTypes.length === 1)) {
      return;
    }

    const nextTypes = TIMELINE_ITEM_TYPES.filter((candidate) =>
      candidate === type ? !selected : state.selectedTypes.includes(candidate),
    );
    updates.cancelResume();
    cancelActive();
    setManualLoadStatus('');
    setState((current) => ({
      ...current,
      selectedTypes: nextTypes,
      items: [],
      nextCursor: null,
      hasMore: false,
      hasLoaded: false,
      pending: null,
      error: null,
    }));
    if (state.isOpen) {
      setManualLoadStatus('Загружаем историю…');
      load('initial', nextTypes, null);
    }
  };

  const handleRefresh = () => {
    if (sessionEnded) {
      return;
    }

    updates.cancelResume();
    setManualLoadStatus(state.hasLoaded ? 'Обновляем историю…' : 'Загружаем историю…');
    load(state.hasLoaded ? 'refresh' : 'initial', state.selectedTypes, null, { source: 'manual-refresh' });
  };

  const handleShowMore = () => {
    if ((activeLoad.current && updates.isLive()) || state.pending !== null || state.nextCursor === null) {
      return;
    }

    updates.cancelResume();
    setManualLoadStatus('Загружаем продолжение истории…');
    load('more', state.selectedTypes, state.nextCursor);
  };

  const handleRetry = (event: MouseEvent<HTMLButtonElement>) => {
    const button = event.currentTarget;
    // Запуск чтения сразу убирает кнопку ошибки из DOM.
    if (button.ownerDocument.activeElement === button) refreshButton.current?.focus();
    handleRefresh();
  };

  const { isOpen, selectedTypes, items, hasLoaded, pending, error } = state;
  const canShowMore =
    hasLoaded && state.hasMore && state.nextCursor !== null && pending === null && error === null;
  const allTypesSelected = selectedTypes.length === TIMELINE_ITEM_TYPES.length;
  const retry =
    error === null
      ? null
      : error.phase === 'more'
        ? { label: 'Повторить загрузку продолжения', run: handleShowMore }
        : { label: 'Повторить', run: handleRetry };

  return (
    <section className="panel position-timeline" aria-labelledby={titleId}>
      <p className="screen-reader-only" role="status" aria-atomic="true">{manualLoadStatus}</p>
      <header className="position-timeline-header">
        <h2 id={titleId} className="section-title">
          <button
            type="button"
            className="position-timeline-toggle"
            aria-expanded={isOpen}
            aria-controls={contentId}
            onClick={handleToggleOpen}
          >
            История позиции
          </button>
        </h2>
        {isOpen && !sessionEnded && (
          <button ref={refreshButton} type="button" className="button button-secondary" onClick={handleRefresh}>
            Обновить
          </button>
        )}
      </header>
      <div id={contentId} className="position-timeline-content" hidden={!isOpen}>
        <fieldset className="position-timeline-filters" aria-describedby={filterHintId}>
          <legend>Типы событий</legend>
          {TIMELINE_ITEM_TYPES.map((type) => {
            const checked = selectedTypes.includes(type);
            return (
              <label key={type} className="position-timeline-filter">
                <input
                  type="checkbox"
                  checked={checked}
                  disabled={sessionEnded || (checked && selectedTypes.length === 1)}
                  onChange={() => handleToggleType(type)}
                />
                {TIMELINE_FILTER_LABELS[type]}
              </label>
            );
          })}
          <p id={filterHintId} className="muted position-timeline-hint">
            Должен быть выбран хотя бы один тип событий.
          </p>
        </fieldset>

        {(pending?.phase === 'initial' || pending?.phase === 'refresh') && (
          <p className="inline-status" aria-hidden="true">
            <span
              key={pending.source}
              className={pending.source === 'manual-refresh' ? 'spinner spinner--continuous' : 'spinner'}
              aria-hidden="true"
            />
            {pending.phase === 'initial' ? 'Загружаем историю…' : 'Обновляем историю…'}
          </p>
        )}

        {error !== null && error.phase !== 'more' && (
          <TimelineAlert
            notice={error.notice}
            note={error.phase === 'refresh' && items.length > 0 ? 'Показана ранее загруженная история.' : null}
            retry={retry}
            onLogin={onLogin}
          />
        )}

        {hasLoaded && items.length === 0 && pending === null && error === null && (
          <p className="muted position-timeline-empty">
            {allTypesSelected ? 'В истории позиции пока нет событий.' : 'Событий выбранных типов нет.'}
          </p>
        )}

        {items.length > 0 && (
          <ol className="position-timeline-list" aria-label="События истории позиции">
            {items.map((item) => (
              <li key={timelineItemKey(item)}>
                <PositionTimelineItem item={item} settlementAsset={settlementAsset} />
              </li>
            ))}
          </ol>
        )}

        {pending?.phase === 'more' && (
          <p className="inline-status" aria-hidden="true">
            <span className="spinner" aria-hidden="true" />
            Загружаем продолжение истории…
          </p>
        )}

        {error?.phase === 'more' && (
          <TimelineAlert
            notice={error.notice}
            note="Ранее загруженные события сохранены."
            retry={retry}
            onLogin={onLogin}
          />
        )}

        {canShowMore && (
          <button type="button" className="button button-secondary position-timeline-more" onClick={handleShowMore}>
            Показать ещё
          </button>
        )}
      </div>
    </section>
  );
}

function applyPage(
  current: TimelineState,
  phase: LoadPhase,
  requestCursor: string | null,
  page: PositionTimelinePage,
): TimelineState {
  if (phase !== 'more') {
    return {
      ...current,
      items: page.items,
      nextCursor: page.nextCursor,
      hasMore: page.hasMore,
      hasLoaded: true,
      pending: null,
      error: null,
    };
  }

  // Неизменный cursor при hasMore=true привёл бы к бесконечному повтору одной страницы.
  if (page.hasMore && page.nextCursor === requestCursor) {
    return {
      ...current,
      pending: null,
      error: {
        phase,
        notice: {
          text: 'Получено некорректное продолжение истории. Обновите историю.',
          traceId: null,
          sessionEnded: false,
          canRetry: false,
        },
      },
    };
  }

  return {
    ...current,
    items: appendUnique(current.items, page.items),
    nextCursor: page.nextCursor,
    hasMore: page.hasMore,
    pending: null,
    error: null,
  };
}

interface TimelineAlertProps {
  notice: TimelineNotice;
  note: string | null;
  retry: { label: string; run: (event: MouseEvent<HTMLButtonElement>) => void } | null;
  onLogin: () => void;
}

function TimelineAlert({ notice, note, retry, onLogin }: TimelineAlertProps) {
  return (
    <div className="alert" role="alert">
      <p className="notice-text">{notice.text}</p>
      {note && <p className="notice-text">{note}</p>}
      {notice.traceId && <p className="notice-trace">Код диагностики: {notice.traceId}</p>}
      {notice.sessionEnded && (
        <button type="button" className="button button-secondary" onClick={onLogin}>
          Войти снова
        </button>
      )}
      {notice.canRetry && retry !== null && (
        <button type="button" className="button button-secondary" onClick={retry.run}>
          {retry.label}
        </button>
      )}
    </div>
  );
}
