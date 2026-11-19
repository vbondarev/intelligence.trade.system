import type { BrowserSession } from './authTypes';

export const LOGIN_URL = '/bff/auth/login?returnUrl=/app';

const SESSION_URL = '/bff/auth/session';
const SESSION_TIMING_URL = '/bff/auth/session/timing';
const SESSION_EXTEND_URL = '/bff/auth/session/extend';
const ANTIFORGERY_URL = '/bff/auth/antiforgery';
const LOGOUT_URL = '/bff/auth/logout';
const CSRF_HEADER = 'X-CSRF-TOKEN';

export class AuthApiError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = 'AuthApiError';
    this.status = status;
  }
}

export async function fetchSession(signal?: AbortSignal): Promise<BrowserSession> {
  const response = await fetch(SESSION_URL, {
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
    signal,
  });
  if (!response.ok) {
    throw new AuthApiError('Session request failed.', response.status);
  }

  return parseSession(await response.json());
}

export function startLogin(): void {
  window.location.assign(LOGIN_URL);
}

export function startLoginAt(returnUrl: string): void {
  window.location.assign('/bff/auth/login?' + new URLSearchParams({ returnUrl }).toString());
}

async function fetchAntiforgeryToken(signal?: AbortSignal): Promise<string> {
  const tokenResponse = await fetch(ANTIFORGERY_URL, {
    credentials: 'same-origin',
    cache: 'no-store',
    headers: { Accept: 'application/json' },
    signal,
  });
  if (!tokenResponse.ok) {
    throw new AuthApiError('Antiforgery request failed.', tokenResponse.status);
  }

  const { requestToken } = (await tokenResponse.json()) as { requestToken?: unknown };
  if (typeof requestToken !== 'string' || requestToken.length === 0) {
    throw new AuthApiError('Antiforgery response is malformed.', tokenResponse.status);
  }

  return requestToken;
}

export async function logout(): Promise<void> {
  const requestToken = await fetchAntiforgeryToken();
  const logoutResponse = await fetch(LOGOUT_URL, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { Accept: 'application/json', [CSRF_HEADER]: requestToken },
  });
  if (!logoutResponse.ok) {
    throw new AuthApiError('Logout request failed.', logoutResponse.status);
  }

  const { redirectUrl } = (await logoutResponse.json()) as { redirectUrl?: unknown };
  if (typeof redirectUrl !== 'string' || !isSameOriginPath(redirectUrl)) {
    throw new AuthApiError('Logout redirect is not a same-origin path.', logoutResponse.status);
  }

  window.location.assign(redirectUrl);
}

export interface SessionTiming {
  expiresAt: number;
  remainingMs: number;
}

export function fetchSessionTiming(signal: AbortSignal): Promise<SessionTiming | null> {
  return requestSessionTiming(SESSION_TIMING_URL, { signal });
}

export async function extendSession(signal: AbortSignal): Promise<SessionTiming | null> {
  try {
    const token = await fetchAntiforgeryToken(signal);
    return await requestSessionTiming(SESSION_EXTEND_URL, {
      method: 'POST',
      headers: { [CSRF_HEADER]: token },
      signal,
    });
  } catch (error) {
    if (error instanceof AuthApiError && error.status === 401) return null;
    throw error;
  }
}

async function requestSessionTiming(url: string, init: RequestInit): Promise<SessionTiming | null> {
  const started = performance.now();
  const response = await fetch(url, {
    ...init,
    headers: { Accept: 'application/json', ...init.headers },
    credentials: 'same-origin',
    cache: 'no-store',
  });
  if (response.status === 401) return null;
  if (!response.ok) throw new AuthApiError('Session timing request failed.', response.status);

  const { serverTimeUtc, expiresAtUtc } = (await response.json()) as {
    serverTimeUtc?: unknown;
    expiresAtUtc?: unknown;
  };
  const serverTime = typeof serverTimeUtc === 'string' ? Date.parse(serverTimeUtc) : NaN;
  const expiresAt = typeof expiresAtUtc === 'string' ? Date.parse(expiresAtUtc) : NaN;
  if (!Number.isFinite(serverTime) || !Number.isFinite(expiresAt) || expiresAt < serverTime) {
    throw new AuthApiError('Session timing response is malformed.', response.status);
  }

  // Ориентируемся на разницу серверных часов, вычитая время запроса с запасом.
  return { expiresAt, remainingMs: Math.max(0, expiresAt - serverTime - (performance.now() - started)) };
}

function isSameOriginPath(value: string): boolean {
  return value.startsWith('/') && !value.startsWith('//') && !value.includes('\\');
}

function parseSession(value: unknown): BrowserSession {
  if (typeof value === 'object' && value !== null && 'authenticated' in value) {
    if (value.authenticated === false) {
      return { authenticated: false };
    }

    if (value.authenticated === true && 'user' in value) {
      const user = value.user;
      if (
        typeof user === 'object' &&
        user !== null &&
        'userId' in user &&
        'subject' in user &&
        typeof user.userId === 'string' &&
        typeof user.subject === 'string'
      ) {
        return { authenticated: true, user: { userId: user.userId, subject: user.subject } };
      }
    }
  }

  throw new AuthApiError('Session response is malformed.', 200);
}
