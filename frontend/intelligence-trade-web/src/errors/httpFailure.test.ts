import { describe, expect, it } from 'vitest';
import { decodeProblem, HttpFailure, readSuccessJson } from './httpFailure';
import { presentError } from './errorPresentation';

describe('declarative HTTP errors', () => {
  it('takes only known diagnostic properties and skips unsafe field keys', async () => {
    const body = '{"code":"validation_failed","traceId":"trace","title":"secret","detail":"raw","errors":{"name":["message",1],"__proto__":["unsafe"],"constructor":["unsafe"]}}';
    const response = new Response(body, { status: 400, headers: { 'Content-Type': 'application/problem+json; charset=utf-8' } });
    expect(await decodeProblem(response)).toEqual({
      code: 'validation_failed', traceId: 'trace', reason: null, errors: { name: ['message'] },
    });
  });

  it.each([
    new Response(null, { status: 401 }),
    new Response('<html>', { status: 503, headers: { 'Content-Type': 'text/html' } }),
    new Response('{', { status: 503, headers: { 'Content-Type': 'application/json' } }),
    new Response('[]', { status: 503, headers: { 'Content-Type': 'application/json' } }),
  ])('reads BFF errors without usable ProblemDetails', async (response) => {
    expect(await decodeProblem(response)).toEqual({ code: null, traceId: null, reason: null, errors: null });
  });

  it('propagates a cancellation and identifies an invalid success response', async () => {
    const controller = new AbortController();
    controller.abort();
    const invalid = () => new HttpFailure('invalid', 200);
    await expect(decodeProblem(new Response('{'), controller.signal)).rejects.toBe(controller.signal.reason);
    await expect(readSuccessJson(new Response('{'), invalid)).rejects.toBeInstanceOf(HttpFailure);
    expect(presentError(invalid(), { fallback: 'fallback' }).kind).toBe('invalid-response');
  });

  it.each([
    [400, 'validation'], [401, 'session-ended'], [403, 'forbidden'],
    [404, 'missing'], [409, 'conflict'], [503, 'unavailable'], [500, 'internal'],
  ])('classifies HTTP %i independently of raw messages', (status, kind) => {
    const context = { fallback: 'fallback', retry: true };
    const failure = new HttpFailure('raw', status, 'unrecognized', 'trace');
    expect(presentError(failure, context)).toMatchObject({ kind, code: null, traceId: 'trace' });
    expect(presentError(new HttpFailure('another', status, 'unrecognized', 'trace'), context))
      .toEqual(presentError(failure, context));
  });

  it('preserves operation-specific codes only for compatible statuses', () => {
    const context = { fallback: 'fallback', retry: true };
    expect(presentError(new HttpFailure('raw', 403, 'exchange_permissions_rejected'), context))
      .toMatchObject({ kind: 'forbidden', code: 'exchange_permissions_rejected', action: null });
    expect(presentError(new HttpFailure('raw', 401, 'exchange_permissions_rejected'), context))
      .toMatchObject({ kind: 'session-ended', code: null, action: 'login' });
  });
});
