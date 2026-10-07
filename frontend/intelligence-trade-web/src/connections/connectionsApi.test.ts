import { describe, expect, it } from 'vitest';
import { jsonResponse, mockFetch } from '../test/fetchMock';
import {
  ConnectionsApiError,
  createConnection,
  disconnectConnection,
  listConnections,
  renameConnection,
  restoreConnection,
  rotateCredentials,
  syncConnection,
  verifyConnection,
} from './connectionsApi';
import type { ExchangeAccount } from './connectionTypes';

const ACCOUNT_ID = '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6';
const ITEM_URL = `/bff/me/exchange-accounts/${ACCOUNT_ID}`;
const FAKE_CREDENTIALS = { apiKey: 'fake-api-key', apiSecret: 'fake-api-secret' };

const account: ExchangeAccount = {
  id: ACCOUNT_ID,
  displayName: 'Основной',
  exchange: 'bybit',
  connectionStatus: 'connected',
  capabilities: ['readBalance', 'readPositions'],
  lastSyncedAt: '2026-10-06T10:00:00Z',
};

function problemResponse(status: number, body: Record<string, unknown>): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

const antiforgery = () => jsonResponse({ requestToken: 'csrf-token' });

describe('connectionsApi', () => {
  it('lists connections through the same-origin BFF without caching', async () => {
    const disabled = { ...account, id: 'other', displayName: 'GinArea', connectionStatus: 'disabled' };
    const requests = mockFetch({
      '/bff/me/exchange-accounts': () => jsonResponse({ items: [account, disabled] }),
    });

    await expect(listConnections()).resolves.toEqual([account, disabled]);
    expect(requests).toHaveLength(1);
    expect(requests[0]?.init?.credentials).toBe('same-origin');
    expect(requests[0]?.init?.cache).toBe('no-store');
    expect(requests[0]?.init?.method).toBeUndefined();
  });

  it('rejects a malformed list response', async () => {
    mockFetch({ '/bff/me/exchange-accounts': () => jsonResponse({ items: [{ id: 1 }] }) });

    await expect(listConnections()).rejects.toBeInstanceOf(ConnectionsApiError);
  });

  it('creates a connection with antiforgery token and reports 201 as a new connection', async () => {
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      '/bff/me/exchange-accounts': () => jsonResponse(account, 201),
    });

    const result = await createConnection('Основной', FAKE_CREDENTIALS);

    expect(result).toEqual({ outcome: 'created', account });
    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery', '/bff/me/exchange-accounts']);
    const create = requests[1]?.init;
    expect(create?.method).toBe('POST');
    expect(create?.credentials).toBe('same-origin');
    const headers = new Headers(create?.headers);
    expect(headers.get('X-CSRF-TOKEN')).toBe('csrf-token');
    expect(headers.get('Content-Type')).toBe('application/json');
    expect(JSON.parse(create?.body as string)).toEqual({
      displayName: 'Основной',
      exchange: 'bybit',
      apiKey: 'fake-api-key',
      apiSecret: 'fake-api-secret',
    });
    expect(requests.every((request) => request.url.startsWith('/bff/'))).toBe(true);
  });

  it('reports 200 from create as a reconnect of an existing connection', async () => {
    mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      '/bff/me/exchange-accounts': () => jsonResponse(account, 200),
    });

    await expect(createConnection('Другое', FAKE_CREDENTIALS)).resolves.toEqual({ outcome: 'reconnected', account });
  });

  it('restores a disabled connection through the same POST with its stored display name', async () => {
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      '/bff/me/exchange-accounts': () => jsonResponse(account, 200),
    });

    const result = await restoreConnection({ displayName: 'GinArea' }, FAKE_CREDENTIALS);

    expect(result.outcome).toBe('reconnected');
    expect(requests[1]?.init?.method).toBe('POST');
    expect(JSON.parse(requests[1]?.init?.body as string)).toMatchObject({ displayName: 'GinArea', exchange: 'bybit' });
  });

  it('does not treat 201 from restore as restoring the selected connection', async () => {
    mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      '/bff/me/exchange-accounts': () => jsonResponse({ ...account, id: 'new-account' }, 201),
    });

    const result = await restoreConnection({ displayName: 'GinArea' }, FAKE_CREDENTIALS);

    expect(result.outcome).toBe('created');
  });

  it('renames, verifies, rotates credentials and disconnects through explicit BFF routes', async () => {
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      [ITEM_URL]: (init) =>
        init?.method === 'DELETE' ? new Response(null, { status: 204 }) : jsonResponse(account),
      [`${ITEM_URL}/verify`]: () => jsonResponse(account),
      [`${ITEM_URL}/credentials`]: () => jsonResponse(account),
    });

    await renameConnection(ACCOUNT_ID, 'GinArea');
    await verifyConnection(ACCOUNT_ID);
    await rotateCredentials(ACCOUNT_ID, FAKE_CREDENTIALS);
    await disconnectConnection(ACCOUNT_ID);

    const mutations = requests.filter((request) => request.url !== '/bff/auth/antiforgery');
    expect(mutations.map((request) => `${request.init?.method} ${request.url}`)).toEqual([
      `PATCH ${ITEM_URL}`,
      `POST ${ITEM_URL}/verify`,
      `PUT ${ITEM_URL}/credentials`,
      `DELETE ${ITEM_URL}`,
    ]);
    expect(requests.filter((request) => request.url === '/bff/auth/antiforgery')).toHaveLength(4);
    expect(mutations.every((request) => new Headers(request.init?.headers).get('X-CSRF-TOKEN') === 'csrf-token')).toBe(
      true,
    );
    expect(JSON.parse(mutations[0]?.init?.body as string)).toEqual({ displayName: 'GinArea' });
    expect(mutations[1]?.init?.body).toBeUndefined();
    expect(JSON.parse(mutations[2]?.init?.body as string)).toEqual(FAKE_CREDENTIALS);
    expect(mutations[3]?.init?.body).toBeUndefined();
  });

  it('keeps only machine-readable ProblemDetails fields', async () => {
    mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      '/bff/me/exchange-accounts': () =>
        problemResponse(409, {
          type: 'urn:intelligence-trade:error:exchange-account-already-exists',
          title: 'The exchange account already exists.',
          detail: 'Human readable text',
          status: 409,
          code: 'exchange_account_already_exists',
          traceId: '00-trace-01',
          errors: { displayName: ['invalid'] },
        }),
    });

    const error = await createConnection('Основной', FAKE_CREDENTIALS).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ConnectionsApiError);
    const apiError = error as ConnectionsApiError;
    expect(apiError.status).toBe(409);
    expect(apiError.code).toBe('exchange_account_already_exists');
    expect(apiError.traceId).toBe('00-trace-01');
    expect(apiError.errors).toEqual({ displayName: ['invalid'] });
    expect(apiError.message).not.toContain('Human readable text');
    expect(apiError.message).not.toContain('fake-api');
  });

  it('reports a BFF status without body as an error without code', async () => {
    mockFetch({ '/bff/me/exchange-accounts': () => new Response(null, { status: 401 }) });

    const error = (await listConnections().catch((caught: unknown) => caught)) as ConnectionsApiError;

    expect(error).toBeInstanceOf(ConnectionsApiError);
    expect(error.status).toBe(401);
    expect(error.code).toBeNull();
  });

  it('does not send the mutation when the antiforgery token cannot be obtained', async () => {
    const requests = mockFetch({ '/bff/auth/antiforgery': () => jsonResponse({}, 401) });

    const error = (await verifyConnection(ACCOUNT_ID).catch((caught: unknown) => caught)) as ConnectionsApiError;

    expect(error.status).toBe(401);
    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery']);
  });

  it('syncs a connection through the explicit BFF route with antiforgery token and without body', async () => {
    const synced = { ...account, lastSyncedAt: '2026-10-07T10:00:00Z' };
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      [`${ITEM_URL}/sync`]: () => jsonResponse(synced),
    });

    await expect(syncConnection(ACCOUNT_ID)).resolves.toEqual(synced);
    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery', `${ITEM_URL}/sync`]);
    const sync = requests[1]?.init;
    expect(sync?.method).toBe('POST');
    expect(sync?.credentials).toBe('same-origin');
    expect(sync?.cache).toBe('no-store');
    expect(sync?.body).toBeUndefined();
    expect(new Headers(sync?.headers).get('X-CSRF-TOKEN')).toBe('csrf-token');
  });

  it.each([
    [409, 'exchange_account_disabled'],
    [503, 'exchange_unavailable'],
    [404, 'resource_not_found'],
  ])('reports sync ProblemDetails %s as a typed error without retry', async (status, code) => {
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      [`${ITEM_URL}/sync`]: () => problemResponse(status, { status, code, traceId: '00-trace-02', detail: 'secret detail' }),
    });

    const error = (await syncConnection(ACCOUNT_ID).catch((caught: unknown) => caught)) as ConnectionsApiError;

    expect(error).toBeInstanceOf(ConnectionsApiError);
    expect(error.status).toBe(status);
    expect(error.code).toBe(code);
    expect(error.traceId).toBe('00-trace-02');
    expect(error.message).not.toContain('secret detail');
    expect(requests.filter((request) => request.url === `${ITEM_URL}/sync`)).toHaveLength(1);
  });

  it('does not retry sync after a network failure', async () => {
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      [`${ITEM_URL}/sync`]: () => {
        throw new TypeError('network down');
      },
    });

    await expect(syncConnection(ACCOUNT_ID)).rejects.toBeInstanceOf(TypeError);
    expect(requests.filter((request) => request.url === `${ITEM_URL}/sync`)).toHaveLength(1);
  });

  it('does not send sync when the antiforgery token cannot be obtained', async () => {
    const requests = mockFetch({ '/bff/auth/antiforgery': () => jsonResponse({}, 401) });

    const error = (await syncConnection(ACCOUNT_ID).catch((caught: unknown) => caught)) as ConnectionsApiError;

    expect(error.status).toBe(401);
    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery']);
  });

  it('encodes the connection id as a single path segment', async () => {
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      '/bff/me/exchange-accounts/..%2Fauth%2Flogout': () => new Response(null, { status: 404 }),
    });

    await expect(disconnectConnection('../auth/logout')).rejects.toBeInstanceOf(ConnectionsApiError);
    expect(requests[1]?.url).toBe('/bff/me/exchange-accounts/..%2Fauth%2Flogout');
  });
});
