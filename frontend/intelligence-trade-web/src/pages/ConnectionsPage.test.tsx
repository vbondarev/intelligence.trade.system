import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import { AuthProvider } from '../auth/AuthProvider';
import { AppRoutes } from '../app/router';
import type { ExchangeAccount } from '../connections/connectionTypes';
import { jsonResponse, mockFetch, stubLocationAssign, type RecordedRequest, type RouteHandler } from '../test/fetchMock';

const CONNECTIONS_PATH = '/app/settings/connections';
const COLLECTION_URL = '/bff/me/exchange-accounts';
const MAIN_ID = '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6';
const DISABLED_ID = '6b1d2c3e-4f5a-4b6c-8d7e-9f0a1b2c3d4e';
const FAKE_KEY = 'fake-api-key-not-a-real-bybit-key';
const FAKE_SECRET = 'fake-api-secret-not-a-real-bybit-secret';
const EMPTY_STATE = 'Подключений пока нет. Добавьте read-only ключ Bybit.';

const authenticatedSession = {
  authenticated: true,
  user: { userId: '7a0c8b5e-6c1d-4f3e-9a6b-2d4e8f1a3c5b', subject: '7a0c8b5e-6c1d-4f3e-9a6b-2d4e8f1a3c5b' },
};

const mainAccount: ExchangeAccount = {
  id: MAIN_ID,
  displayName: 'Основной',
  exchange: 'bybit',
  connectionStatus: 'connected',
  capabilities: ['readBalance', 'readPositions'],
  lastSyncedAt: '2026-10-06T10:00:00Z',
};

const disabledAccount: ExchangeAccount = {
  id: DISABLED_ID,
  displayName: 'GinArea',
  exchange: 'bybit',
  connectionStatus: 'disabled',
  capabilities: [],
  lastSyncedAt: null,
};

function problem(status: number, code: string, traceId = '00-trace-01'): Response {
  return new Response(
    JSON.stringify({
      type: `urn:intelligence-trade:error:${code}`,
      title: 'Server title must not be shown',
      detail: 'Server detail must not be shown',
      status,
      code,
      traceId,
    }),
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );
}

function renderAtConnections() {
  render(
    <AuthProvider>
      <MemoryRouter initialEntries={[CONNECTIONS_PATH]}>
        <AppRoutes />
      </MemoryRouter>
    </AuthProvider>,
  );
}

/**
 * Поднимает страницу с mock BFF: `GET` collection отдаёт текущее значение `list()`,
 * остальные методы collection обрабатывает `onCollection`.
 */
function renderConnections(options: {
  list: () => ExchangeAccount[] | Response | Promise<Response>;
  onCollection?: RouteHandler;
  routes?: Record<string, RouteHandler>;
}): RecordedRequest[] {
  const requests = mockFetch({
    '/bff/auth/session': () => jsonResponse(authenticatedSession),
    '/bff/auth/antiforgery': () => jsonResponse({ requestToken: 'csrf-token' }),
    [COLLECTION_URL]: (init) => {
      if (init?.method === undefined || init.method === 'GET') {
        const value = options.list();
        return Array.isArray(value) ? jsonResponse({ items: value }) : value;
      }

      if (options.onCollection === undefined) {
        throw new Error(`Unexpected ${init.method} ${COLLECTION_URL}`);
      }

      return options.onCollection(init);
    },
    ...options.routes,
  });

  renderAtConnections();
  return requests;
}

function listRequests(requests: RecordedRequest[]): RecordedRequest[] {
  return requests.filter((request) => request.url === COLLECTION_URL && request.init?.method === undefined);
}

function mutations(requests: RecordedRequest[]): RecordedRequest[] {
  return requests.filter((request) => request.url.startsWith(COLLECTION_URL) && request.init?.method !== undefined);
}

function bodyOf(request: RecordedRequest | undefined): unknown {
  return JSON.parse(request?.init?.body as string);
}

function findCard(name: string): Promise<HTMLElement> {
  return screen.findByRole('article', { name });
}

/**
 * Promise, который тест завершает явно: порядок ответов задаётся тестом, а не таймерами.
 */
function deferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((settle) => {
    resolve = settle;
  });
  return { promise, resolve };
}

describe('ConnectionsPage routing', () => {
  it('redirects an anonymous visitor to the landing', async () => {
    mockFetch({ '/bff/auth/session': () => jsonResponse({ authenticated: false }) });

    renderAtConnections();

    expect(await screen.findByRole('button', { name: 'Войти' })).toBeVisible();
    expect(screen.queryByRole('heading', { name: 'Подключения' })).not.toBeInTheDocument();
  });

  it('shows the connections page inside the authenticated shell', async () => {
    renderConnections({ list: () => [] });

    expect(await screen.findByRole('heading', { name: 'Подключения', level: 1 })).toBeVisible();
    const navigation = screen.getByRole('navigation', { name: 'Основная навигация' });
    expect(within(navigation).getByRole('link', { name: 'Обзор' })).toHaveAttribute('href', '/app');
    expect(within(navigation).getByRole('link', { name: 'Подключения' })).toHaveAttribute('href', CONNECTIONS_PATH);
  });
});

describe('ConnectionsPage list', () => {
  it('shows loading state while connections are being loaded', async () => {
    mockFetch({
      '/bff/auth/session': () => jsonResponse(authenticatedSession),
      [COLLECTION_URL]: () => new Promise<Response>(() => {}),
    });

    renderAtConnections();

    expect(await screen.findByText('Загружаем подключения…')).toHaveAttribute('role', 'status');
  });

  it('shows active and disabled connections with their details', async () => {
    const reserve: ExchangeAccount = { ...mainAccount, id: 'third', displayName: 'Резерв', connectionStatus: 'unknown' };
    renderConnections({ list: () => [mainAccount, disabledAccount, reserve] });

    const main = await findCard('Основной');
    expect(within(main).getByText('Подключено')).toBeVisible();
    expect(within(main).getByText('Bybit')).toBeVisible();
    expect(within(main).getByText('Чтение баланса, Чтение позиций')).toBeVisible();
    expect(main.querySelector('time')).toHaveAttribute('dateTime', '2026-10-06T10:00:00Z');

    const disabled = await findCard('GinArea');
    expect(within(disabled).getByText('Отключено')).toBeVisible();
    expect(within(disabled).getByText('Синхронизаций ещё не было')).toBeVisible();

    expect(within(await findCard('Резерв')).getByText('Не проверено')).toBeVisible();
    expect(screen.getAllByRole('article')).toHaveLength(3);
  });

  it('shows an empty state when there are no connections', async () => {
    renderConnections({ list: () => [] });

    expect(await screen.findByText(EMPTY_STATE)).toBeVisible();
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
  });

  it('shows a mapped load error with trace id and without server text', async () => {
    renderConnections({ list: () => problem(503, 'exchange_unavailable', '00-load-trace') });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Bybit сейчас недоступен');
    expect(alert).toHaveTextContent('00-load-trace');
    expect(alert).not.toHaveTextContent('Server detail must not be shown');
    expect(alert).not.toHaveTextContent('Server title must not be shown');
  });

  it('offers to sign in again when the BFF session has ended', async () => {
    const assign = stubLocationAssign();
    renderConnections({ list: () => new Response(null, { status: 401 }) });

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Сессия завершена');
    await userEvent.click(within(alert).getByRole('button', { name: 'Войти снова' }));

    expect(assign).toHaveBeenCalledWith('/bff/auth/login?returnUrl=/app');
  });
});

describe('ConnectionsPage stale list responses', () => {
  /**
   * Первый `GET` остаётся pending, пользователь создаёт подключение, а reload после mutation
   * успевает завершиться раньше первого `GET`.
   */
  async function createWhileInitialListIsPending(initialList: Promise<Response>): Promise<void> {
    let items: ExchangeAccount[] = [];
    let listCalls = 0;
    const requests = renderConnections({
      list: () => (++listCalls === 1 ? initialList : items),
      onCollection: () => {
        items = [mainAccount];
        return jsonResponse(mainAccount, 201);
      },
    });
    const user = userEvent.setup();
    expect(await screen.findByText('Загружаем подключения…')).toBeVisible();

    await user.type(screen.getByLabelText('Название *'), 'Основной');
    await user.type(screen.getByLabelText('API key *'), FAKE_KEY);
    await user.type(screen.getByLabelText('API secret *'), FAKE_SECRET);
    await user.click(screen.getByRole('button', { name: 'Подключить' }));

    expect(await findCard('Основной')).toBeVisible();
    expect(listRequests(requests)).toHaveLength(2);
  }

  it('keeps the newer reload when the initial list resolves later with stale data', async () => {
    const initialList = deferred<Response>();
    await createWhileInitialListIsPending(initialList.promise);

    const staleResponse = jsonResponse({ items: [] });
    await act(async () => {
      initialList.resolve(staleResponse);
    });

    expect(staleResponse.bodyUsed).toBe(true);
    expect(screen.getByRole('article', { name: 'Основной' })).toBeVisible();
    expect(screen.queryByText(EMPTY_STATE)).not.toBeInTheDocument();
  });

  it('does not show an error from the initial list that fails after a newer successful reload', async () => {
    const initialList = deferred<Response>();
    await createWhileInitialListIsPending(initialList.promise);

    const staleFailure = problem(503, 'exchange_unavailable', '00-stale-trace');
    await act(async () => {
      initialList.resolve(staleFailure);
    });

    expect(staleFailure.bodyUsed).toBe(true);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(screen.getByRole('article', { name: 'Основной' })).toBeVisible();
    expect(screen.getByRole('status')).toHaveTextContent('Подключение «Основной» добавлено.');
  });
});

describe('ConnectionsPage add', () => {
  it('requires a non-blank display name before sending credentials', async () => {
    const requests = renderConnections({ list: () => [] });
    const user = userEvent.setup();
    await screen.findByText(EMPTY_STATE);

    await user.type(screen.getByLabelText('Название *'), '   ');
    await user.type(screen.getByLabelText('API key *'), FAKE_KEY);
    await user.type(screen.getByLabelText('API secret *'), FAKE_SECRET);
    await user.click(screen.getByRole('button', { name: 'Подключить' }));

    expect(await screen.findByText('Укажите название подключения.')).toBeVisible();
    expect(mutations(requests)).toHaveLength(0);
  });

  it('creates a connection with antiforgery header, clears the form and reloads the list', async () => {
    let items: ExchangeAccount[] = [];
    const requests = renderConnections({
      list: () => items,
      onCollection: () => {
        items = [mainAccount];
        return jsonResponse(mainAccount, 201);
      },
    });
    const user = userEvent.setup();
    await screen.findByText(EMPTY_STATE);

    await user.type(screen.getByLabelText('Название *'), '  Основной  ');
    await user.type(screen.getByLabelText('API key *'), FAKE_KEY);
    await user.type(screen.getByLabelText('API secret *'), FAKE_SECRET);
    await user.click(screen.getByRole('button', { name: 'Подключить' }));

    expect(await findCard('Основной')).toBeVisible();
    expect(screen.getByRole('status')).toHaveTextContent('Подключение «Основной» добавлено.');
    const [create] = mutations(requests);
    expect(create?.init?.method).toBe('POST');
    expect(new Headers(create?.init?.headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
    expect(bodyOf(create)).toEqual({ displayName: 'Основной', exchange: 'bybit', apiKey: FAKE_KEY, apiSecret: FAKE_SECRET });
    expect(listRequests(requests)).toHaveLength(2);
    expect(screen.getByLabelText('Название *')).toHaveValue('');
    expect(screen.getByLabelText('API key *')).toHaveValue('');
    expect(screen.getByLabelText('API secret *')).toHaveValue('');
  });

  it('reports 200 from create as a reconnect with the stored name', async () => {
    renderConnections({
      list: () => [],
      onCollection: () => jsonResponse({ ...mainAccount, displayName: 'Старое имя' }, 200),
    });
    const user = userEvent.setup();
    await screen.findByText(EMPTY_STATE);

    await user.type(screen.getByLabelText('Название *'), 'Новое имя');
    await user.type(screen.getByLabelText('API key *'), FAKE_KEY);
    await user.type(screen.getByLabelText('API secret *'), FAKE_SECRET);
    await user.click(screen.getByRole('button', { name: 'Подключить' }));

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Этот аккаунт Bybit уже был подключён как «Старое имя»: подключение восстановлено с прежним названием.',
    );
  });

  it('clears secrets but keeps the name when create is rejected', async () => {
    renderConnections({ list: () => [], onCollection: () => problem(400, 'exchange_permissions_rejected') });
    const user = userEvent.setup();
    await screen.findByText(EMPTY_STATE);

    await user.type(screen.getByLabelText('Название *'), 'Основной');
    await user.type(screen.getByLabelText('API key *'), FAKE_KEY);
    await user.type(screen.getByLabelText('API secret *'), FAKE_SECRET);
    await user.click(screen.getByRole('button', { name: 'Подключить' }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Права ключа не подходят');
    expect(alert).toHaveTextContent('00-trace-01');
    expect(alert).not.toHaveTextContent('Server detail must not be shown');
    expect(screen.getByLabelText('Название *')).toHaveValue('Основной');
    expect(screen.getByLabelText('API key *')).toHaveValue('');
    expect(screen.getByLabelText('API secret *')).toHaveValue('');
    expect(document.body).not.toHaveTextContent(FAKE_SECRET);
  });
});

describe('ConnectionsPage rename', () => {
  it('renames through PATCH and reloads the list', async () => {
    let items = [mainAccount];
    const requests = renderConnections({
      list: () => items,
      routes: {
        [`${COLLECTION_URL}/${MAIN_ID}`]: () => {
          items = [{ ...mainAccount, displayName: 'Инвестиции' }];
          return jsonResponse(items[0]);
        },
      },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Переименовать' }));
    const input = screen.getByLabelText('Новое название *');
    expect(input).toHaveValue('Основной');
    await user.clear(input);
    await user.type(input, '  Инвестиции ');
    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await findCard('Инвестиции')).toBeVisible();
    expect(screen.getByRole('status')).toHaveTextContent('Подключение переименовано в «Инвестиции».');
    const [rename] = mutations(requests);
    expect(rename?.init?.method).toBe('PATCH');
    expect(bodyOf(rename)).toEqual({ displayName: 'Инвестиции' });
    expect(listRequests(requests)).toHaveLength(2);
  });

  it('validates the new name before sending', async () => {
    const requests = renderConnections({ list: () => [mainAccount] });
    const user = userEvent.setup();

    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Переименовать' }));
    await user.clear(screen.getByLabelText('Новое название *'));
    await user.type(screen.getByLabelText('Новое название *'), '  ');
    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByText('Укажите название подключения.')).toBeVisible();
    expect(mutations(requests)).toHaveLength(0);
  });

  it('allows renaming a disabled connection', async () => {
    const requests = renderConnections({
      list: () => [disabledAccount],
      routes: {
        [`${COLLECTION_URL}/${DISABLED_ID}`]: () => jsonResponse({ ...disabledAccount, displayName: 'Архив' }),
      },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('GinArea')).getByRole('button', { name: 'Переименовать' }));
    await user.clear(screen.getByLabelText('Новое название *'));
    await user.type(screen.getByLabelText('Новое название *'), 'Архив');
    await user.click(screen.getByRole('button', { name: 'Сохранить' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Подключение переименовано в «Архив».');
    expect(mutations(requests).map((request) => request.init?.method)).toEqual(['PATCH']);
  });
});

describe('ConnectionsPage verify', () => {
  it('verifies through POST and reloads the list', async () => {
    let items = [mainAccount];
    const requests = renderConnections({
      list: () => items,
      routes: {
        [`${COLLECTION_URL}/${MAIN_ID}/verify`]: () => {
          items = [{ ...mainAccount, connectionStatus: 'unavailable' }];
          return jsonResponse(items[0]);
        },
      },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Проверить' }));

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Подключение «Основной» проверено. Статус: Недоступно.',
    );
    await waitFor(() => expect(listRequests(requests)).toHaveLength(2));
    expect(mutations(requests).map((request) => `${request.init?.method} ${request.url}`)).toEqual([
      `POST ${COLLECTION_URL}/${MAIN_ID}/verify`,
    ]);
    expect(mutations(requests)[0]?.init?.body).toBeUndefined();
  });

  it('reloads the list after a failed verification', async () => {
    const requests = renderConnections({
      list: () => [mainAccount],
      routes: { [`${COLLECTION_URL}/${MAIN_ID}/verify`]: () => problem(503, 'exchange_unavailable') },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Проверить' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Bybit сейчас недоступен');
    await waitFor(() => expect(listRequests(requests)).toHaveLength(2));
  });
});

describe('ConnectionsPage credential rotation', () => {
  it('replaces credentials through PUT and clears the inputs', async () => {
    const requests = renderConnections({
      list: () => [mainAccount],
      routes: { [`${COLLECTION_URL}/${MAIN_ID}/credentials`]: () => jsonResponse(mainAccount) },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Заменить ключи' }));
    expect(screen.queryByLabelText('Новое название *')).not.toBeInTheDocument();
    await user.type(screen.getByLabelText('Новый API key *'), FAKE_KEY);
    await user.type(screen.getByLabelText('Новый API secret *'), FAKE_SECRET);
    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Заменить ключи' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Ключи подключения «Основной» заменены.');
    const [rotate] = mutations(requests);
    expect(rotate?.init?.method).toBe('PUT');
    expect(bodyOf(rotate)).toEqual({ apiKey: FAKE_KEY, apiSecret: FAKE_SECRET });
    expect(screen.queryByLabelText('Новый API secret *')).not.toBeInTheDocument();
    await waitFor(() => expect(listRequests(requests)).toHaveLength(2));
  });

  it('shows identity mismatch as a dedicated error and clears the inputs', async () => {
    renderConnections({
      list: () => [mainAccount],
      routes: {
        [`${COLLECTION_URL}/${MAIN_ID}/credentials`]: () => problem(409, 'exchange_account_identity_mismatch'),
      },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Заменить ключи' }));
    await user.type(screen.getByLabelText('Новый API key *'), FAKE_KEY);
    await user.type(screen.getByLabelText('Новый API secret *'), FAKE_SECRET);
    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Заменить ключи' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Новые ключи принадлежат другому аккаунту Bybit.');
    expect(screen.getByLabelText('Новый API key *')).toHaveValue('');
    expect(screen.getByLabelText('Новый API secret *')).toHaveValue('');
  });
});

describe('ConnectionsPage disconnect', () => {
  it('does not disconnect when the confirmation is cancelled', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    const requests = renderConnections({ list: () => [mainAccount] });
    const user = userEvent.setup();

    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Отключить' }));

    expect(confirm).toHaveBeenCalledTimes(1);
    expect(confirm.mock.calls[0]?.[0]).toContain('История не удаляется');
    expect(confirm.mock.calls[0]?.[0]).toContain('восстановить');
    expect(mutations(requests)).toHaveLength(0);
  });

  it('disconnects after confirmation and keeps the connection visible as disabled', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    let items = [mainAccount];
    const requests = renderConnections({
      list: () => items,
      routes: {
        [`${COLLECTION_URL}/${MAIN_ID}`]: () => {
          items = [{ ...mainAccount, connectionStatus: 'disabled' }];
          return new Response(null, { status: 204 });
        },
      },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('Основной')).getByRole('button', { name: 'Отключить' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Подключение «Основной» отключено.');
    const card = await findCard('Основной');
    await waitFor(() => expect(within(card).getByText('Отключено')).toBeVisible());
    expect(within(card).getByRole('button', { name: 'Восстановить' })).toBeVisible();
    expect(mutations(requests).map((request) => request.init?.method)).toEqual(['DELETE']);
    expect(listRequests(requests)).toHaveLength(2);
  });
});

describe('ConnectionsPage restore', () => {
  it('offers only rename and restore for a disabled connection', async () => {
    renderConnections({ list: () => [disabledAccount] });

    const card = await findCard('GinArea');
    expect(within(card).getByRole('button', { name: 'Переименовать' })).toBeVisible();
    expect(within(card).getByRole('button', { name: 'Восстановить' })).toBeVisible();
    expect(within(card).queryByRole('button', { name: 'Проверить' })).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Заменить ключи' })).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'Отключить' })).not.toBeInTheDocument();
  });

  it('restores through the create POST with the stored name and reports 200 as a reconnect', async () => {
    let items = [disabledAccount];
    const requests = renderConnections({
      list: () => items,
      onCollection: () => {
        items = [{ ...disabledAccount, connectionStatus: 'connected' }];
        return jsonResponse(items[0], 200);
      },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('GinArea')).getByRole('button', { name: 'Восстановить' }));
    const card = await findCard('GinArea');
    expect(within(card).queryByLabelText(/Название/)).not.toBeInTheDocument();
    await user.type(within(card).getByLabelText('API key *'), FAKE_KEY);
    await user.type(within(card).getByLabelText('API secret *'), FAKE_SECRET);
    await user.click(within(card).getByRole('button', { name: 'Восстановить' }));

    expect(await screen.findByRole('status')).toHaveTextContent('Подключение «GinArea» восстановлено.');
    const [restore] = mutations(requests);
    expect(restore?.init?.method).toBe('POST');
    expect(restore?.url).toBe(COLLECTION_URL);
    expect(bodyOf(restore)).toEqual({ displayName: 'GinArea', exchange: 'bybit', apiKey: FAKE_KEY, apiSecret: FAKE_SECRET });
    await waitFor(() => expect(within(card).getByText('Подключено')).toBeVisible());
  });

  it('does not report the selected connection as restored when the API creates a new one', async () => {
    const created: ExchangeAccount = { ...mainAccount, id: 'new-account', displayName: 'GinArea' };
    let items = [disabledAccount];
    renderConnections({
      list: () => items,
      onCollection: () => {
        items = [disabledAccount, created];
        return jsonResponse(created, 201);
      },
    });
    const user = userEvent.setup();

    await user.click(within(await findCard('GinArea')).getByRole('button', { name: 'Восстановить' }));
    const card = await findCard('GinArea');
    await user.type(within(card).getByLabelText('API key *'), FAKE_KEY);
    await user.type(within(card).getByLabelText('API secret *'), FAKE_SECRET);
    await user.click(within(card).getByRole('button', { name: 'Восстановить' }));

    const status = await screen.findByRole('status');
    expect(status).toHaveTextContent('создано новое подключение «GinArea»');
    expect(status).toHaveTextContent('Подключение «GinArea» осталось отключённым.');
    expect(status).not.toHaveTextContent('восстановлено');
    await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(2));
    expect(screen.getByText('Отключено')).toBeVisible();
  });
});
