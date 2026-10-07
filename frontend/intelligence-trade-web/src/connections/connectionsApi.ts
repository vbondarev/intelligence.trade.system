import type {
  ConnectionResult,
  ExchangeAccount,
  ExchangeAccountCapability,
  ExchangeAccountConnectionStatus,
  ExchangeCredentials,
} from './connectionTypes';

const COLLECTION_URL = '/bff/me/exchange-accounts';
const ANTIFORGERY_URL = '/bff/auth/antiforgery';
const CSRF_HEADER = 'X-CSRF-TOKEN';

const STATUSES: readonly ExchangeAccountConnectionStatus[] = ['unknown', 'connected', 'unavailable', 'disabled'];
const CAPABILITIES: readonly ExchangeAccountCapability[] = ['readBalance', 'readPositions'];

/**
 * Ошибка management API подключений. Хранит только machine-readable поля ProblemDetails:
 * UI ветвится по `status` и `code`, а не по `title`/`detail`.
 */
export class ConnectionsApiError extends Error {
  readonly status: number;
  readonly code: string | null;
  readonly traceId: string | null;
  readonly errors: Readonly<Record<string, readonly string[]>> | null;

  constructor(
    status: number,
    code: string | null = null,
    traceId: string | null = null,
    errors: Readonly<Record<string, readonly string[]>> | null = null,
  ) {
    super(`Connections request failed with status ${status}${code ? ` (${code})` : ''}.`);
    this.name = 'ConnectionsApiError';
    this.status = status;
    this.code = code;
    this.traceId = traceId;
    this.errors = errors;
  }
}

export async function listConnections(signal?: AbortSignal): Promise<ExchangeAccount[]> {
  const response = await fetch(COLLECTION_URL, {
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
    signal,
  });
  if (!response.ok) {
    throw await toError(response);
  }

  const body: unknown = await response.json();
  if (!isObject(body) || !Array.isArray(body.items) || !body.items.every(isExchangeAccount)) {
    throw new ConnectionsApiError(response.status);
  }

  return body.items;
}

export async function createConnection(
  displayName: string,
  credentials: ExchangeCredentials,
): Promise<ConnectionResult> {
  const response = await sendUnsafe('POST', COLLECTION_URL, {
    displayName,
    exchange: 'bybit',
    apiKey: credentials.apiKey,
    apiSecret: credentials.apiSecret,
  });
  const account = await readAccount(response);
  return { outcome: response.status === 201 ? 'created' : 'reconnected', account };
}

/**
 * Восстанавливает отключённое подключение тем же `POST`, что и создание, с сохранённым
 * названием. Backend определяет аккаунт по проверенной provider identity, а не по выбранному
 * подключению: ключи другого аккаунта Bybit приводят к `created`, и выбранное подключение
 * остаётся отключённым.
 */
export function restoreConnection(
  account: Pick<ExchangeAccount, 'displayName'>,
  credentials: ExchangeCredentials,
): Promise<ConnectionResult> {
  return createConnection(account.displayName, credentials);
}

export async function renameConnection(id: string, displayName: string): Promise<ExchangeAccount> {
  return readAccount(await sendUnsafe('PATCH', itemUrl(id), { displayName }));
}

export async function verifyConnection(id: string): Promise<ExchangeAccount> {
  return readAccount(await sendUnsafe('POST', `${itemUrl(id)}/verify`));
}

export async function rotateCredentials(id: string, credentials: ExchangeCredentials): Promise<ExchangeAccount> {
  return readAccount(
    await sendUnsafe('PUT', `${itemUrl(id)}/credentials`, {
      apiKey: credentials.apiKey,
      apiSecret: credentials.apiSecret,
    }),
  );
}

export async function disconnectConnection(id: string): Promise<void> {
  await sendUnsafe('DELETE', itemUrl(id));
}

function itemUrl(id: string): string {
  return `${COLLECTION_URL}/${encodeURIComponent(id)}`;
}

async function sendUnsafe(method: string, url: string, body?: object): Promise<Response> {
  const requestToken = await fetchAntiforgeryToken();
  const headers: Record<string, string> = { Accept: 'application/json', [CSRF_HEADER]: requestToken };
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(url, {
    method,
    credentials: 'same-origin',
    cache: 'no-store',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) {
    throw await toError(response);
  }

  return response;
}

async function fetchAntiforgeryToken(): Promise<string> {
  const response = await fetch(ANTIFORGERY_URL, {
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) {
    throw new ConnectionsApiError(response.status);
  }

  const body: unknown = await response.json();
  if (!isObject(body) || typeof body.requestToken !== 'string' || body.requestToken.length === 0) {
    throw new ConnectionsApiError(response.status);
  }

  return body.requestToken;
}

async function readAccount(response: Response): Promise<ExchangeAccount> {
  const body: unknown = await response.json();
  if (!isExchangeAccount(body)) {
    throw new ConnectionsApiError(response.status);
  }

  return body;
}

async function toError(response: Response): Promise<ConnectionsApiError> {
  const contentType = response.headers.get('Content-Type') ?? '';
  if (!/json/i.test(contentType)) {
    return new ConnectionsApiError(response.status);
  }

  let body: unknown;
  try {
    body = await response.json();
  } catch {
    return new ConnectionsApiError(response.status);
  }

  if (!isObject(body)) {
    return new ConnectionsApiError(response.status);
  }

  return new ConnectionsApiError(
    response.status,
    typeof body.code === 'string' ? body.code : null,
    typeof body.traceId === 'string' ? body.traceId : null,
    parseErrors(body.errors),
  );
}

function parseErrors(value: unknown): Record<string, string[]> | null {
  if (!isObject(value)) {
    return null;
  }

  const errors: Record<string, string[]> = {};
  for (const [field, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) {
      errors[field] = messages.filter((message): message is string => typeof message === 'string');
    }
  }

  return errors;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isExchangeAccount(value: unknown): value is ExchangeAccount {
  return (
    isObject(value) &&
    typeof value.id === 'string' &&
    typeof value.displayName === 'string' &&
    value.exchange === 'bybit' &&
    typeof value.connectionStatus === 'string' &&
    (STATUSES as readonly string[]).includes(value.connectionStatus) &&
    Array.isArray(value.capabilities) &&
    value.capabilities.every(
      (capability) => typeof capability === 'string' && (CAPABILITIES as readonly string[]).includes(capability),
    ) &&
    (value.lastSyncedAt === null || typeof value.lastSyncedAt === 'string')
  );
}
