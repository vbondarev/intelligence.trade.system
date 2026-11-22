import { decodeProblem, HttpFailure, readSuccessJson } from '../errors/httpFailure';
import type { BrowserSession } from './authTypes';

export const LOGIN_URL = '/bff/auth/login?returnUrl=/app';

const SESSION_URL = '/bff/auth/session';
const SESSION_TIMING_URL = '/bff/auth/session/timing';
const SESSION_EXTEND_URL = '/bff/auth/session/extend';
const ANTIFORGERY_URL = '/bff/auth/antiforgery';
const LOGOUT_URL = '/bff/auth/logout';
const CSRF_HEADER = 'X-CSRF-TOKEN';

export class AuthApiError extends HttpFailure {

  constructor(message: string, status: number, code: string | null = null, traceId: string | null = null) {
    super(message, status, code, traceId);
    this.name = 'AuthApiError';
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
    throw await authFailure('Session request failed.', response, signal);
  }

  return parseSession(await readAuthJson(response, signal), response.status);
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
    throw await authFailure('Antiforgery request failed.', tokenResponse, signal);
  }

  const { requestToken } = asFields(await readAuthJson(tokenResponse, signal));
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
    throw await authFailure('Logout request failed.', logoutResponse);
  }

  const { redirectUrl } = asFields(await readAuthJson(logoutResponse));
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
  if (!response.ok) throw await authFailure('Session timing request failed.', response, init.signal);

  const { serverTimeUtc, expiresAtUtc } = asFields(await readAuthJson(response, init.signal));
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

function parseSession(value: unknown, status: number): BrowserSession {
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

  throw new AuthApiError('Session response is malformed.', status);
}

async function authFailure(message: string, response: Response, signal?: AbortSignal | null): Promise<AuthApiError> {
  const { code, traceId } = await decodeProblem(response, signal);
  return new AuthApiError(message, response.status, code, traceId);
}

function readAuthJson(response: Response, signal?: AbortSignal | null): Promise<unknown> {
  return readSuccessJson(response, () => new AuthApiError('Auth response is malformed.', response.status), signal);
}

function asFields(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as Record<string, unknown> : {};
}
