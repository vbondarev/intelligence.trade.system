import { useEffect, useId, useRef, useState } from 'react';
import { PositionApiError, TIMELINE_ITEM_TYPES, getPositionTimeline } from './positionApi';
import { PositionTimelineItem } from './PositionTimelineItem';
import { TIMELINE_FILTER_LABELS, timelineItemKey } from './positionTimelineFormatting';
import type { PositionTimelineItem as TimelineItem, PositionTimelineItemType, PositionTimelinePage } from './positionTypes';

const SESSION_ENDED = 'Сессия завершена. Войдите снова.';
const SERVICE_UNAVAILABLE = 'Сервис временно недоступен. Повторите попытку позже.';

type LoadPhase = 'initial' | 'more' | 'refresh';

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
  pending: LoadPhase | null;
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
  const titleId = useId();
  const contentId = useId();
  const filterHintId = useId();
  const [state, setState] = useState<TimelineState>(INITIAL_STATE);
  // После завершения сессии доступ восстанавливает только повторный вход: ни одна команда
  // блока больше не отправляет timeline-запрос.
  const sessionEnded = state.error?.notice.sessionEnded === true;

  const generation = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const inFlight = useRef(false);

  useEffect(
    () => () => {
      generation.current += 1;
      controller.current?.abort();
    },
    [],
  );

  const cancelActive = () => {
    generation.current += 1;
    controller.current?.abort();
    controller.current = null;
    inFlight.current = false;
  };

  // Ответ применяется, только если запрос не отменён и его поколение осталось текущим:
  // одной отмены недостаточно для защиты от уже начатого разбора ответа.
  const load = (phase: LoadPhase, types: readonly PositionTimelineItemType[], cursor: string | null) => {
    cancelActive();
    const requestController = new AbortController();
    const requestGeneration = generation.current;
    controller.current = requestController;
    inFlight.current = true;
    const isCurrent = () => requestGeneration === generation.current && !requestController.signal.aborted;
    const finish = () => {
      controller.current = null;
      inFlight.current = false;
    };

    setState((current) => ({
      ...current,
      pending: phase,
      error: null,
      // Cursor прежнего поколения не комбинируется со страницами нового.
      ...(phase === 'more' ? {} : { nextCursor: null, hasMore: false }),
    }));

    getPositionTimeline(positionId, { types, cursor }, requestController.signal).then(
      (page) => {
        if (!isCurrent()) {
          return;
        }

        finish();
        setState((current) => applyPage(current, phase, cursor, page));
      },
      (error: unknown) => {
        if (!isCurrent()) {
          return;
        }

        finish();
        setState((current) => ({ ...current, pending: null, error: { phase, notice: toNotice(error, phase) } }));
      },
    );
  };

  const handleToggleOpen = () => {
    if (state.isOpen) {
      if (state.pending === 'initial') {
        cancelActive();
        setState((current) => ({ ...current, isOpen: false, pending: null }));
        return;
      }

      setState((current) => ({ ...current, isOpen: false }));
      return;
    }

    setState((current) => ({ ...current, isOpen: true }));
    if (!state.hasLoaded && state.pending === null && state.error?.notice.canRetry !== false) {
      load('initial', state.selectedTypes, null);
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
    cancelActive();
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
      load('initial', nextTypes, null);
    }
  };

  const handleRefresh = () => {
    if (sessionEnded) {
      return;
    }

    load(state.hasLoaded ? 'refresh' : 'initial', state.selectedTypes, null);
  };

  const handleShowMore = () => {
    if (inFlight.current || state.nextCursor === null) {
      return;
    }

    load('more', state.selectedTypes, state.nextCursor);
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
        : { label: 'Повторить', run: handleRefresh };

  return (
    <section className="panel position-timeline" aria-labelledby={titleId}>
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
          <button type="button" className="button button-secondary" onClick={handleRefresh}>
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

        {(pending === 'initial' || pending === 'refresh') && (
          <p className="inline-status" role="status">
            <span className="spinner" aria-hidden="true" />
            {pending === 'initial' ? 'Загружаем историю…' : 'Обновляем историю…'}
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

        {pending === 'more' && (
          <p className="inline-status" role="status">
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
  retry: { label: string; run: () => void } | null;
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
