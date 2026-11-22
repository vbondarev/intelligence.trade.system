import { describe, expect, it } from 'vitest';
import { decodeProblem, HttpFailure, readSuccessJson } from './httpFailure';
import { presentError } from './errorPresentation';

describe('Декодирование и классификация HTTP-ошибок', () => {
  it('читает только известные диагностические признаки и пропускает небезопасные ключи', async () => {
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
  ])('обрабатывает ошибки BFF без пригодного ProblemDetails', async (response) => {
    expect(await decodeProblem(response)).toEqual({ code: null, traceId: null, reason: null, errors: null });
  });

  it('передаёт отмену и распознаёт некорректный успешный ответ', async () => {
    const controller = new AbortController();
    controller.abort();
    const invalid = () => new HttpFailure('invalid', 200);
    await expect(decodeProblem(new Response('{'), controller.signal)).rejects.toBe(controller.signal.reason);
    await expect(readSuccessJson(new Response('{'), invalid)).rejects.toBeInstanceOf(HttpFailure);
    expect(presentError(invalid(), { fallback: 'fallback' }).kind).toBe('invalid-response');
  });

  it('сохраняет сетевую ошибку чтения тела и статус неуспешного ответа', async () => {
    const outage = new TypeError('Сбой потока ответа');
    const response = (status: number) => new Response(new ReadableStream({
      start(controller) { controller.error(outage); },
    }), { status, headers: { 'Content-Type': 'application/json' } });
    const invalid = () => new HttpFailure('Некорректный ответ', 200);

    await expect(readSuccessJson(response(200), invalid)).rejects.toBe(outage);
    await expect(decodeProblem(response(503))).resolves.toEqual({
      code: null, traceId: null, reason: null, errors: null,
    });
    const used = new Response('{}');
    await used.text();
    await expect(readSuccessJson(used, invalid)).rejects.toBeInstanceOf(HttpFailure);
  });

  it.each([
    [400, 'validation'], [401, 'session-ended'], [403, 'forbidden'],
    [404, 'missing'], [409, 'conflict'], [503, 'unavailable'], [500, 'internal'],
  ])('классифицирует HTTP %i независимо от сырого текста', (status, kind) => {
    const context = { fallback: 'fallback', retry: true };
    const failure = new HttpFailure('raw', status, 'unrecognized', 'trace');
    expect(presentError(failure, context)).toMatchObject({ kind, code: null, traceId: 'trace' });
    expect(presentError(new HttpFailure('another', status, 'unrecognized', 'trace'), context))
      .toEqual(presentError(failure, context));
  });

  it('учитывает предметные коды только при совместимом статусе', () => {
    const context = { fallback: 'fallback', retry: true };
    expect(presentError(new HttpFailure('raw', 403, 'exchange_permissions_rejected'), context))
      .toMatchObject({ kind: 'forbidden', code: 'exchange_permissions_rejected', action: null });
    expect(presentError(new HttpFailure('raw', 401, 'exchange_permissions_rejected'), context))
      .toMatchObject({ kind: 'session-ended', code: null, action: 'login' });
  });
});
