import { vi } from 'vitest';

export interface RecordedRequest {
  url: string;
  init: RequestInit | undefined;
}

export type RouteHandler = (init: RequestInit | undefined) => Response | Promise<Response>;

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

/**
 * Подменяет global fetch маршрутами по URL и записывает запросы в порядке вызова.
 */
export function mockFetch(routes: Record<string, RouteHandler>): RecordedRequest[] {
  const requests: RecordedRequest[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      requests.push({ url, init });
      const handler = routes[url];
      if (handler === undefined) {
        throw new Error(`Unexpected fetch: ${url}`);
      }

      return handler(init);
    }),
  );

  return requests;
}

export function stubLocationAssign() {
  const assign = vi.fn();
  vi.stubGlobal('location', { ...window.location, assign });
  return assign;
}
