import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { jsonResponse, mockFetch, type RecordedRequest, type RouteHandler } from '../test/fetchMock';
import {
  CLOSED_POSITION_CHANGE,
  INCREASED_POSITION_CHANGE,
  POSITION_ID,
  TIMELINE_EVALUATION,
  TIMELINE_RECOMMENDATION,
  continuationTimelinePage,
  emptyTimelinePage,
  firstTimelinePage,
  timelinePageFixture,
  timelineRecommendationItem,
} from '../test/positionFixtures';
import { PositionTimeline } from './PositionTimeline';
import type { PositionTimelinePage } from './positionTypes';

const BASE = `/bff/me/positions/${POSITION_ID}/timeline?pageSize=25`;
const FIRST_CURSOR = 'cursor+page/2==';
const timelineRequestUrl = (query = '') => `${BASE}${query}`;
const cursorQuery = (cursor: string) => `&cursor=${encodeURIComponent(cursor)}`;
const MORE_URL = timelineRequestUrl(cursorQuery(FIRST_CURSOR));
const WITHOUT_POSITION_CHANGES_URL = timelineRequestUrl('&type=evaluation&type=recommendation');
const EVALUATIONS_ONLY_URL = timelineRequestUrl('&type=evaluation');

function problem(status: number, code: string): Response {
  return new Response(
    JSON.stringify({ title: 'Server title must not be shown', detail: 'secret detail', status, code, traceId: '00-trace-09' }),
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );
}

function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((settle) => {
    resolve = settle;
  });
  return { promise, resolve };
}

/** Отвечает по очереди; последний ответ повторяется для последующих запросов. */
function sequence(...handlers: RouteHandler[]): RouteHandler {
  let index = 0;
  return (init) => {
    const handler = handlers[Math.min(index, handlers.length - 1)];
    index += 1;
    return handler!(init);
  };
}

const page = (body: PositionTimelinePage): RouteHandler => () => jsonResponse(body);

function renderTimeline(routes: Record<string, RouteHandler>) {
  const requests = mockFetch(routes);
  const onLogin = vi.fn();
  const view = render(<PositionTimeline positionId={POSITION_ID} settlementAsset="USDT" onLogin={onLogin} />);
  return { requests, onLogin, view };
}

const toggle = () => screen.getByRole('button', { name: 'История позиции' });
const events = () => within(screen.getByRole('list', { name: 'События истории позиции' })).getAllByRole('article');
const summaries = () => events().map((article) => within(article).getByRole('heading', { level: 3 }).textContent);
const findEvents = async (count: number) => {
  await waitFor(() => expect(screen.queryAllByRole('article')).toHaveLength(count));
  return events();
};
const settle = () => act(() => new Promise((resolve) => setTimeout(resolve, 0)));
const requestUrls = (requests: RecordedRequest[]) => requests.map((request) => request.url);
const filter = (name: string) => screen.getByRole('checkbox', { name });

afterEach(() => {
  vi.clearAllMocks();
});

describe('PositionTimeline lazy loading', () => {
  it('renders a collapsed block and does not request history on mount', async () => {
    const { requests } = renderTimeline({ [BASE]: page(firstTimelinePage()) });

    expect(screen.getByRole('region', { name: 'История позиции' })).toBeVisible();
    expect(toggle()).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('group', { name: 'Типы событий' })).not.toBeInTheDocument();
    await settle();
    expect(requests).toHaveLength(0);
  });

  it('requests the first page once on the first open and shows all three event types newest-first', async () => {
    const response = deferred<Response>();
    const { requests } = renderTimeline({ [BASE]: () => response.promise });

    await userEvent.click(toggle());

    expect(toggle()).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('status')).toHaveTextContent('Загружаем историю…');
    response.resolve(jsonResponse(firstTimelinePage()));

    const articles = await findEvents(3);
    expect(requestUrls(requests)).toEqual([BASE]);
    expect(requests[0]?.init?.method).toBeUndefined();
    expect(within(articles[0]!).getByRole('heading', { level: 3 })).toHaveTextContent('Наблюдать');
    expect(within(articles[0]!).getByText('Рекомендация')).toBeVisible();
    expect(within(articles[1]!).getByRole('heading', { level: 3 })).toHaveTextContent(
      'Данные: устаревшие. Увеличение риска заблокировано.',
    );
    expect(within(articles[2]!).getByRole('heading', { level: 3 })).toHaveTextContent(/Размер позиции уменьшился: 0,3\s→\s0,1/);
    expect(within(articles[2]!).getByText('Изменение позиции')).toBeVisible();
    expect(articles[0]!.querySelector('time')).toHaveAttribute('dateTime', TIMELINE_RECOMMENDATION.occurredAt);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('keeps the loaded history when the block is closed and reopened without a new request', async () => {
    const { requests } = renderTimeline({ [BASE]: page(firstTimelinePage()) });
    await userEvent.click(toggle());
    await findEvents(3);

    await userEvent.click(toggle());
    expect(toggle()).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('list', { name: 'События истории позиции' })).not.toBeInTheDocument();

    await userEvent.click(toggle());
    expect(await findEvents(3)).toHaveLength(3);
    expect(requests).toHaveLength(1);
  });

  it('opens and closes by keyboard and links the toggle to the controlled content', async () => {
    renderTimeline({ [BASE]: page(firstTimelinePage()) });
    const user = userEvent.setup();

    await user.tab();
    expect(toggle()).toHaveFocus();
    await user.keyboard('{Enter}');
    await findEvents(3);

    const contentId = toggle().getAttribute('aria-controls');
    expect(contentId).toBeTruthy();
    expect(document.getElementById(contentId!)).toContainElement(screen.getByRole('list', { name: 'События истории позиции' }));

    await user.keyboard(' ');
    expect(toggle()).toHaveAttribute('aria-expanded', 'false');
  });

  it('cancels the request and ignores its response when unmounted', async () => {
    const response = deferred<Response>();
    const { requests, view } = renderTimeline({ [BASE]: () => response.promise });
    await userEvent.click(toggle());

    view.unmount();
    response.resolve(jsonResponse(firstTimelinePage()));
    await settle();

    expect((requests[0]?.init?.signal as AbortSignal).aborted).toBe(true);
  });

  it('only reads history: no evaluation, sync or other requests', async () => {
    const { requests } = renderTimeline({
      [BASE]: page(firstTimelinePage()),
      [MORE_URL]: page(continuationTimelinePage()),
    });
    await userEvent.click(toggle());
    await findEvents(3);
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));
    await findEvents(5);
    await userEvent.click(screen.getByRole('button', { name: 'Обновить' }));
    await settle();

    expect(requests.every((request) => request.url.startsWith(`/bff/me/positions/${POSITION_ID}/timeline?`))).toBe(true);
    expect(requests.every((request) => (request.init?.method ?? 'GET') === 'GET')).toBe(true);
  });
});

describe('PositionTimeline event details', () => {
  async function openDetails(article: HTMLElement) {
    await userEvent.click(within(article).getByText('Подробности'));
  }

  it('expands a position change with before and after snapshots and the observation disclaimer', async () => {
    renderTimeline({ [BASE]: page(firstTimelinePage(null)) });
    await userEvent.click(toggle());
    const reduced = (await findEvents(3))[2]!;

    expect(within(reduced).getByText('Номер изменения')).not.toBeVisible();
    await openDetails(reduced);

    expect(
      within(reduced).getByText(
        'Изменение зафиксировано по наблюдению позиции и не является подтверждённым ордером или исполнением.',
      ),
    ).toBeVisible();
    const beforeRows = within(reduced).getByLabelText('Состояние до изменения');
    const afterRows = within(reduced).getByLabelText('Состояние после изменения');
    expect(within(beforeRows).getByText('Размер').nextElementSibling).toHaveTextContent('0,3');
    expect(within(afterRows).getByText('Размер').nextElementSibling).toHaveTextContent('0,1');
    expect(within(afterRows).getByText('Нереализованный PnL').nextElementSibling).toHaveTextContent(/−15,5\sUSDT/);
    expect(within(reduced).getByText('Номер изменения').nextElementSibling).toHaveTextContent('3');
  });

  it('shows a missing previous state instead of reconstructing it', async () => {
    renderTimeline({ [BASE]: page(continuationTimelinePage()) });
    await userEvent.click(toggle());
    const created = (await findEvents(2))[1]!;
    await openDetails(created);

    expect(within(created).getByRole('heading', { level: 3 })).toHaveTextContent('Позиция обнаружена');
    expect(within(created).getByText('Предыдущее состояние не сохранено.')).toBeVisible();
    expect(within(created).queryByLabelText('Состояние до изменения')).not.toBeInTheDocument();
  });

  it('keeps null snapshot values as a dash instead of zeros', async () => {
    renderTimeline({ [BASE]: page(timelinePageFixture([CLOSED_POSITION_CHANGE])) });
    await userEvent.click(toggle());
    const closed = (await findEvents(1))[0]!;
    await openDetails(closed);

    const afterRows = within(closed).getByLabelText('Состояние после изменения');
    for (const label of ['Средняя цена входа', 'Стоимость', 'Нереализованный PnL', 'Плечо', 'Цена ликвидации']) {
      expect(within(afterRows).getByText(label).nextElementSibling).toHaveTextContent('—');
    }
    expect(within(afterRows).getByText('Размер').nextElementSibling).toHaveTextContent('0');
    expect(within(closed).getByText('Состояние после изменения', { selector: 'dt' }).nextElementSibling).toHaveTextContent(
      'Закрыта',
    );
  });

  it('shows the historical evaluation with its quality and reasons without an expiry warning', async () => {
    renderTimeline({ [BASE]: page(timelinePageFixture([TIMELINE_EVALUATION])) });
    await userEvent.click(toggle());
    const evaluation = (await findEvents(1))[0]!;
    await openDetails(evaluation);

    expect(within(evaluation).getByText('assessment-v1')).toBeVisible();
    const quality = within(evaluation).getByRole('list', { name: 'Качество входных данных исторической оценки' });
    expect(within(quality).getByText(/^Портфель:/)).toBeVisible();
    const reasons = within(evaluation).getByRole('list', { name: 'Причины исторической оценки' });
    expect(within(reasons).getByText('futureReasonCode')).toBeVisible();
    expect(evaluation).not.toHaveTextContent(/истекл|устарела/i);
  });

  it('shows recommendation confidence as a fraction and missing priority as a dash', async () => {
    const withConfidence = timelineRecommendationItem('2026-10-12T08:00:00Z', {
      id: 'c0000000-0000-4000-8000-000000000002',
      confidence: 0.65,
      priority: 'high',
      action: 'hold',
      status: 'active',
    });
    renderTimeline({ [BASE]: page(timelinePageFixture([withConfidence, TIMELINE_RECOMMENDATION])) });
    await userEvent.click(toggle());
    const [current, previous] = await findEvents(2);
    await openDetails(current!);
    await openDetails(previous!);

    expect(within(current!).getByText('Уверенность').nextElementSibling).toHaveTextContent(/^65\s%$/);
    expect(within(current!).getByText('Приоритет').nextElementSibling).not.toHaveTextContent('—');
    expect(within(previous!).getByText('Уверенность').nextElementSibling).toHaveTextContent('—');
    expect(within(previous!).getByText('Приоритет').nextElementSibling).toHaveTextContent('—');
    expect(within(previous!).getByText('Текущий сохранённый статус записи')).toBeVisible();
    expect(within(previous!).getByText('Оценка-основание').nextElementSibling).toHaveTextContent(
      TIMELINE_EVALUATION.evaluation!.id,
    );
  });
});

describe('PositionTimeline pagination', () => {
  it('shows «Показать ещё» only while the API reports more events and appends the continuation', async () => {
    const { requests } = renderTimeline({
      [BASE]: page(firstTimelinePage()),
      [MORE_URL]: page(continuationTimelinePage()),
    });
    await userEvent.click(toggle());
    await findEvents(3);

    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));

    const articles = await findEvents(5);
    expect(requestUrls(requests)).toEqual([BASE, MORE_URL]);
    expect(within(articles[3]!).getByRole('heading', { level: 3 })).toHaveTextContent(/Размер позиции увеличился/);
    expect(within(articles[4]!).getByRole('heading', { level: 3 })).toHaveTextContent('Позиция обнаружена');
    expect(screen.queryByRole('button', { name: 'Показать ещё' })).not.toBeInTheDocument();
  });

  it('does not show «Показать ещё» on the last page', async () => {
    renderTimeline({ [BASE]: page(firstTimelinePage(null)) });
    await userEvent.click(toggle());
    await findEvents(3);

    expect(screen.queryByRole('button', { name: 'Показать ещё' })).not.toBeInTheDocument();
  });

  it('sends a single continuation request on a repeated click', async () => {
    const response = deferred<Response>();
    const { requests } = renderTimeline({ [BASE]: page(firstTimelinePage()), [MORE_URL]: () => response.promise });
    await userEvent.click(toggle());
    await findEvents(3);

    const more = screen.getByRole('button', { name: 'Показать ещё' });
    fireEvent.click(more);
    fireEvent.click(more);
    expect(screen.getByRole('status')).toHaveTextContent('Загружаем продолжение истории…');
    response.resolve(jsonResponse(continuationTimelinePage()));
    await findEvents(5);

    expect(requestUrls(requests)).toEqual([BASE, MORE_URL]);
  });

  it('keeps already loaded events when the continuation fails and retries the same cursor', async () => {
    const { requests } = renderTimeline({
      [BASE]: page(firstTimelinePage()),
      [MORE_URL]: sequence(() => problem(500, 'internal_error'), page(continuationTimelinePage())),
    });
    await userEvent.click(toggle());
    await findEvents(3);
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Не удалось загрузить продолжение истории. Повторите попытку позже.');
    expect(alert).toHaveTextContent('Ранее загруженные события сохранены.');
    expect(alert).toHaveTextContent('Код диагностики: 00-trace-09');
    expect(alert).not.toHaveTextContent('secret detail');
    expect(events()).toHaveLength(3);

    await userEvent.click(within(alert).getByRole('button', { name: 'Повторить загрузку продолжения' }));

    await findEvents(5);
    expect(requestUrls(requests)).toEqual([BASE, MORE_URL, MORE_URL]);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('does not repeat a continuation that returns the same cursor', async () => {
    const { requests } = renderTimeline({
      [BASE]: page(firstTimelinePage()),
      [MORE_URL]: page(continuationTimelinePage(FIRST_CURSOR)),
    });
    await userEvent.click(toggle());
    await findEvents(3);
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Получено некорректное продолжение истории. Обновите историю.');
    expect(within(screen.getByRole('alert')).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Показать ещё' })).not.toBeInTheDocument();
    expect(events()).toHaveLength(3);
    expect(requests).toHaveLength(2);
  });
});

describe('PositionTimeline filters', () => {
  it('requests only the selected types and keeps at least one type selected', async () => {
    const { requests } = renderTimeline({
      [BASE]: page(firstTimelinePage()),
      [WITHOUT_POSITION_CHANGES_URL]: page(timelinePageFixture([TIMELINE_RECOMMENDATION, TIMELINE_EVALUATION])),
      [EVALUATIONS_ONLY_URL]: page(timelinePageFixture([TIMELINE_EVALUATION])),
    });
    await userEvent.click(toggle());
    await findEvents(3);
    expect(screen.getByRole('group', { name: 'Типы событий' })).toHaveAccessibleDescription(
      'Должен быть выбран хотя бы один тип событий.',
    );

    await userEvent.click(filter('Изменения позиции'));
    await findEvents(2);
    await userEvent.click(filter('Рекомендации'));
    await findEvents(1);

    expect(requestUrls(requests)).toEqual([BASE, WITHOUT_POSITION_CHANGES_URL, EVALUATIONS_ONLY_URL]);
    expect(filter('Оценки')).toBeChecked();
    expect(filter('Оценки')).toBeDisabled();
    expect(filter('Изменения позиции')).toBeEnabled();

    await userEvent.click(filter('Оценки'));
    expect(requests).toHaveLength(3);
    expect(filter('Оценки')).toBeChecked();
  });

  it('resets pagination when the filter changes and continues with the new cursor', async () => {
    const filteredCursor = 'evaluations/2';
    const filteredMoreUrl = `${WITHOUT_POSITION_CHANGES_URL}${cursorQuery(filteredCursor)}`;
    const { requests } = renderTimeline({
      [BASE]: page(firstTimelinePage()),
      [MORE_URL]: page(continuationTimelinePage(null)),
      [WITHOUT_POSITION_CHANGES_URL]: page(timelinePageFixture([TIMELINE_RECOMMENDATION], filteredCursor)),
      [filteredMoreUrl]: page(timelinePageFixture([TIMELINE_EVALUATION])),
    });
    await userEvent.click(toggle());
    await findEvents(3);
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));
    await findEvents(5);

    await userEvent.click(filter('Изменения позиции'));
    await findEvents(1);
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));
    await findEvents(2);

    expect(requestUrls(requests)).toEqual([BASE, MORE_URL, WITHOUT_POSITION_CHANGES_URL, filteredMoreUrl]);
  });

  it('ignores a late response of the previous filter', async () => {
    const stale = deferred<Response>();
    const { requests } = renderTimeline({
      [BASE]: () => stale.promise,
      [EVALUATIONS_ONLY_URL]: page(timelinePageFixture([TIMELINE_EVALUATION])),
      [WITHOUT_POSITION_CHANGES_URL]: page(timelinePageFixture([TIMELINE_EVALUATION, TIMELINE_RECOMMENDATION])),
    });
    await userEvent.click(toggle());

    await userEvent.click(filter('Изменения позиции'));
    await findEvents(2);
    stale.resolve(jsonResponse(firstTimelinePage()));
    await settle();

    expect(events()).toHaveLength(2);
    expect(screen.queryByText(/Размер позиции уменьшился/)).not.toBeInTheDocument();
    expect((requests[0]?.init?.signal as AbortSignal).aborted).toBe(true);
  });

  it('distinguishes an empty history from an empty filter result', async () => {
    renderTimeline({
      [BASE]: page(emptyTimelinePage()),
      [WITHOUT_POSITION_CHANGES_URL]: page(emptyTimelinePage()),
    });
    await userEvent.click(toggle());

    expect(await screen.findByText('В истории позиции пока нет событий.')).toBeVisible();
    await userEvent.click(filter('Изменения позиции'));

    expect(await screen.findByText('Событий выбранных типов нет.')).toBeVisible();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});

describe('PositionTimeline refresh', () => {
  it('replaces the loaded pages with a fresh first page and resets the cursor', async () => {
    const freshFirst = timelinePageFixture([CLOSED_POSITION_CHANGE, ...firstTimelinePage().items], 'fresh/2');
    const { requests } = renderTimeline({
      [BASE]: sequence(page(firstTimelinePage()), page(freshFirst)),
      [MORE_URL]: page(continuationTimelinePage()),
      [timelineRequestUrl(cursorQuery('fresh/2'))]: page(continuationTimelinePage()),
    });
    await userEvent.click(toggle());
    await findEvents(3);
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));
    await findEvents(5);

    await userEvent.click(screen.getByRole('button', { name: 'Обновить' }));

    const articles = await findEvents(4);
    expect(within(articles[0]!).getByRole('heading', { level: 3 })).toHaveTextContent('Позиция закрыта');
    await userEvent.click(screen.getByRole('button', { name: 'Показать ещё' }));
    await findEvents(6);
    expect(requestUrls(requests)).toEqual([BASE, MORE_URL, BASE, timelineRequestUrl(cursorQuery('fresh/2'))]);
  });

  it('keeps the previously loaded history on a refresh error and does not continue the old cursor', async () => {
    renderTimeline({
      [BASE]: sequence(page(firstTimelinePage()), () => problem(503, 'service_unavailable')),
    });
    await userEvent.click(toggle());
    await findEvents(3);

    await userEvent.click(screen.getByRole('button', { name: 'Обновить' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Сервис временно недоступен. Повторите попытку позже.');
    expect(alert).toHaveTextContent('Показана ранее загруженная история.');
    expect(events()).toHaveLength(3);
    expect(screen.queryByRole('button', { name: 'Показать ещё' })).not.toBeInTheDocument();
    expect(within(alert).getByRole('button', { name: 'Повторить' })).toBeVisible();
  });

  it('applies only the latest of overlapping refreshes', async () => {
    const slow = deferred<Response>();
    const { requests } = renderTimeline({
      [BASE]: sequence(
        page(firstTimelinePage(null)),
        () => slow.promise,
        page(timelinePageFixture([INCREASED_POSITION_CHANGE])),
      ),
    });
    await userEvent.click(toggle());
    await findEvents(3);

    await userEvent.click(screen.getByRole('button', { name: 'Обновить' }));
    expect(screen.getByRole('status')).toHaveTextContent('Обновляем историю…');
    await userEvent.click(screen.getByRole('button', { name: 'Обновить' }));
    await findEvents(1);
    slow.resolve(jsonResponse(firstTimelinePage(null)));
    await settle();

    expect(events()).toHaveLength(1);
    expect(summaries()[0]).toMatch(/Размер позиции увеличился/);
    expect((requests[1]?.init?.signal as AbortSignal).aborted).toBe(true);
  });
});

describe('PositionTimeline errors', () => {
  it('asks to sign in again after 401 without a retry and keeps it on reopen', async () => {
    const { onLogin, requests } = renderTimeline({ [BASE]: () => new Response(null, { status: 401 }) });
    await userEvent.click(toggle());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Сессия завершена. Войдите снова.');
    expect(within(alert).queryByRole('button', { name: 'Повторить' })).not.toBeInTheDocument();
    await userEvent.click(within(alert).getByRole('button', { name: 'Войти снова' }));
    expect(onLogin).toHaveBeenCalledTimes(1);

    await userEvent.click(toggle());
    await userEvent.click(toggle());
    await settle();
    expect(requests).toHaveLength(1);
  });

  it.each([
    [403, 'access_forbidden', 'Недостаточно прав для просмотра истории позиции.'],
    [404, 'resource_not_found', 'История позиции недоступна.'],
  ])('shows a %s without a retry or server text', async (status, code, text) => {
    renderTimeline({ [BASE]: () => problem(status, code) });
    await userEvent.click(toggle());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(text);
    expect(alert).not.toHaveTextContent('Server title must not be shown');
    expect(within(alert).queryByRole('button')).not.toBeInTheDocument();
    expect(screen.queryByText('В истории позиции пока нет событий.')).not.toBeInTheDocument();
  });

  it.each([
    [400, 'validation_failed', 'Запрос истории отклонён: некорректные параметры запроса.'],
    [500, 'internal_error', 'Не удалось загрузить историю позиции. Повторите попытку позже.'],
    [503, 'service_unavailable', 'Сервис временно недоступен. Повторите попытку позже.'],
  ])('shows a %s with a diagnostic code and a retry of the first page', async (status, code, text) => {
    const { requests } = renderTimeline({
      [BASE]: sequence(() => problem(status, code), page(firstTimelinePage())),
    });
    await userEvent.click(toggle());

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(text);
    expect(alert).toHaveTextContent('Код диагностики: 00-trace-09');
    expect(screen.queryByText('В истории позиции пока нет событий.')).not.toBeInTheDocument();

    await userEvent.click(within(alert).getByRole('button', { name: 'Повторить' }));
    await findEvents(3);
    expect(requestUrls(requests)).toEqual([BASE, BASE]);
  });

  it('reports a contract violation and a network failure explicitly', async () => {
    renderTimeline({
      [BASE]: sequence(
        () => jsonResponse({ items: [], nextCursor: null, hasMore: true }),
        () => {
          throw new TypeError('network down');
        },
      ),
    });
    await userEvent.click(toggle());

    expect(await screen.findByRole('alert')).toHaveTextContent('Получены некорректные данные истории позиции.');
    await userEvent.click(within(screen.getByRole('alert')).getByRole('button', { name: 'Повторить' }));

    await waitFor(() =>
      expect(screen.getByRole('alert')).toHaveTextContent('Сервис временно недоступен. Повторите попытку позже.'),
    );
  });
});
