import { describe, expect, it } from 'vitest';
import { jsonResponse, mockFetch } from '../test/fetchMock';
import {
  POSITION_ID,
  candlesFixture,
  evaluationFixture,
  marketFixture,
  positionFixture,
} from '../test/positionFixtures';
import {
  PositionApiError,
  candlesUrl,
  evaluatePosition,
  getPosition,
  getPositionCandles,
  getPositionEvaluation,
  getPositionMarket,
} from './positionApi';

const ITEM_URL = `/bff/me/positions/${POSITION_ID}`;
const EVALUATION_URL = `${ITEM_URL}/evaluation`;

function problemResponse(status: number, body: Record<string, unknown>): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

const antiforgery = () => jsonResponse({ requestToken: 'csrf-token' });

async function caught(promise: Promise<unknown>): Promise<PositionApiError> {
  const error = await promise.then(
    () => null,
    (reason: unknown) => reason,
  );
  expect(error).toBeInstanceOf(PositionApiError);
  return error as PositionApiError;
}

describe('positionApi reads', () => {
  it('reads the position through the same-origin BFF without caching', async () => {
    const position = positionFixture({ averageEntryPrice: null, unrealizedPnl: -12.5, settlementAsset: 'BTC' });
    const requests = mockFetch({ [ITEM_URL]: () => jsonResponse(position) });

    await expect(getPosition(POSITION_ID)).resolves.toEqual(position);

    expect(requests).toHaveLength(1);
    const init = requests[0]?.init;
    expect(init?.method).toBeUndefined();
    expect(init?.credentials).toBe('same-origin');
    expect(init?.cache).toBe('no-store');
    expect(new Headers(init?.headers).get('Accept')).toBe('application/json');
  });

  it('reads market context and candles through explicit BFF routes', async () => {
    const market = marketFixture();
    const candles = candlesFixture('4h');
    const requests = mockFetch({
      [`${ITEM_URL}/market`]: () => jsonResponse(market),
      [`${ITEM_URL}/candles?interval=4h&limit=200`]: () => jsonResponse(candles),
    });

    await expect(getPositionMarket(POSITION_ID)).resolves.toEqual(market);
    await expect(getPositionCandles(POSITION_ID, '4h')).resolves.toEqual(candles);

    expect(requests.map((request) => request.url)).toEqual([
      `${ITEM_URL}/market`,
      `${ITEM_URL}/candles?interval=4h&limit=200`,
    ]);
  });

  it('encodes the position id as a single path segment', () => {
    expect(candlesUrl('../auth/me', '1h')).toBe('/bff/me/positions/..%2Fauth%2Fme/candles?interval=1h&limit=200');
  });

  it('returns null when the position has not been evaluated yet', async () => {
    mockFetch({ [EVALUATION_URL]: () => new Response(null, { status: 204 }) });

    await expect(getPositionEvaluation(POSITION_ID)).resolves.toBeNull();
  });

  it('accepts an evaluation without result and without recommendation', async () => {
    const base = evaluationFixture();
    const evaluation = evaluationFixture({ assessment: { ...base.assessment, result: null }, recommendation: null });
    mockFetch({ [EVALUATION_URL]: () => jsonResponse(evaluation) });

    await expect(getPositionEvaluation(POSITION_ID)).resolves.toEqual(evaluation);
  });

  it('keeps unknown additive reason codes instead of rejecting the evaluation', async () => {
    const base = evaluationFixture();
    const evaluation = evaluationFixture({
      assessment: { ...base.assessment, reasonCodes: ['futureReasonCode'] },
    });
    mockFetch({ [EVALUATION_URL]: () => jsonResponse(evaluation) });

    await expect(getPositionEvaluation(POSITION_ID)).resolves.toEqual(evaluation);
  });

  it.each([
    ['position with zero-substituted null size', ITEM_URL, () => getPosition(POSITION_ID), { ...positionFixture(), size: null }],
    ['position without settlement asset', ITEM_URL, () => getPosition(POSITION_ID), { ...positionFixture(), settlementAsset: '' }],
    ['position with unknown side', ITEM_URL, () => getPosition(POSITION_ID), { ...positionFixture(), side: 'flat' }],
    [
      'market without timeframe',
      `${ITEM_URL}/market`,
      () => getPositionMarket(POSITION_ID),
      { ...marketFixture(), h4: undefined },
    ],
    [
      'candles with string price',
      `${ITEM_URL}/candles?interval=1h&limit=200`,
      () => getPositionCandles(POSITION_ID, '1h'),
      { ...candlesFixture(), items: [{ ...candlesFixture().items[0], close: '61000' }] },
    ],
    [
      'evaluation with unknown action',
      EVALUATION_URL,
      () => getPositionEvaluation(POSITION_ID),
      (() => {
        const evaluation = evaluationFixture();
        return {
          ...evaluation,
          recommendation: { ...evaluation.recommendation, action: { ...evaluation.recommendation?.action, value: 'buyMore' } },
        };
      })(),
    ],
    [
      'evaluation without data quality',
      EVALUATION_URL,
      () => getPositionEvaluation(POSITION_ID),
      { ...evaluationFixture(), assessment: { ...evaluationFixture().assessment, dataQuality: null } },
    ],
  ])('rejects %s as an explicit error', async (_name, url, call, body) => {
    mockFetch({ [url]: () => jsonResponse(body) });

    const error = await caught(call());

    expect(error.status).toBe(200);
    expect(error.code).toBeNull();
  });

  it('rejects a malformed JSON body as an explicit error', async () => {
    mockFetch({
      [ITEM_URL]: () => new Response('{not json', { status: 200, headers: { 'Content-Type': 'application/json' } }),
    });

    const error = await caught(getPosition(POSITION_ID));

    expect(error.status).toBe(200);
  });

  it('keeps only machine-readable ProblemDetails fields', async () => {
    mockFetch({
      [ITEM_URL]: () =>
        problemResponse(404, { status: 404, code: 'resource_not_found', traceId: '00-trace-01', detail: 'secret detail' }),
    });

    const error = await caught(getPosition(POSITION_ID));

    expect(error.status).toBe(404);
    expect(error.code).toBe('resource_not_found');
    expect(error.traceId).toBe('00-trace-01');
    expect(error.reason).toBeNull();
    expect(error.message).not.toContain('secret detail');
  });

  it('reports a BFF-generated response without body by status only', async () => {
    mockFetch({ [`${ITEM_URL}/market`]: () => new Response(null, { status: 503 }) });

    const error = await caught(getPositionMarket(POSITION_ID));

    expect(error.status).toBe(503);
    expect(error.code).toBeNull();
    expect(error.traceId).toBeNull();
  });

  it('passes the abort signal to fetch', async () => {
    const controller = new AbortController();
    const requests = mockFetch({ [ITEM_URL]: () => jsonResponse(positionFixture()) });

    await getPosition(POSITION_ID, controller.signal);

    expect(requests[0]?.init?.signal).toBe(controller.signal);
  });
});

describe('evaluatePosition', () => {
  it('posts once with antiforgery token and without body', async () => {
    const evaluation = evaluationFixture();
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      [EVALUATION_URL]: () => jsonResponse(evaluation),
    });

    await expect(evaluatePosition(POSITION_ID)).resolves.toEqual(evaluation);

    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery', EVALUATION_URL]);
    const post = requests[1]?.init;
    expect(post?.method).toBe('POST');
    expect(post?.credentials).toBe('same-origin');
    expect(post?.cache).toBe('no-store');
    expect(post?.body).toBeUndefined();
    const headers = new Headers(post?.headers);
    expect(headers.get('X-CSRF-TOKEN')).toBe('csrf-token');
    expect(headers.get('Content-Type')).toBeNull();
  });

  it.each([
    ['closedPosition'],
    ['portfolioUnavailable'],
    ['portfolioInconsistent'],
    ['temporalInconsistency'],
  ])('keeps the not-evaluable reason %s', async (reason) => {
    mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      [EVALUATION_URL]: () =>
        problemResponse(409, { status: 409, code: 'position_not_evaluable', reason, traceId: '00-trace-09' }),
    });

    const error = await caught(evaluatePosition(POSITION_ID));

    expect(error.status).toBe(409);
    expect(error.code).toBe('position_not_evaluable');
    expect(error.reason).toBe(reason);
    expect(error.traceId).toBe('00-trace-09');
  });

  it.each([
    [409, 'position_not_evaluable', 'futureReason'],
    [409, 'concurrency_conflict', 'closedPosition'],
    [503, 'market_data_unavailable', 'closedPosition'],
  ])('does not trust reason %#: status %s code %s', async (status, code, reason) => {
    mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      [EVALUATION_URL]: () => problemResponse(status, { status, code, reason }),
    });

    const error = await caught(evaluatePosition(POSITION_ID));

    expect(error.status).toBe(status);
    expect(error.code).toBe(code);
    expect(error.reason).toBeNull();
  });

  it('does not retry after a network failure', async () => {
    const requests = mockFetch({
      '/bff/auth/antiforgery': antiforgery,
      [EVALUATION_URL]: () => {
        throw new TypeError('network down');
      },
    });

    await expect(evaluatePosition(POSITION_ID)).rejects.toBeInstanceOf(TypeError);
    expect(requests.filter((request) => request.url === EVALUATION_URL)).toHaveLength(1);
  });

  it('does not send the evaluation when the antiforgery token cannot be obtained', async () => {
    const requests = mockFetch({ '/bff/auth/antiforgery': () => jsonResponse({}, 401) });

    const error = await caught(evaluatePosition(POSITION_ID));

    expect(error.status).toBe(401);
    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery']);
  });

  it('does not send the evaluation when the antiforgery response is malformed', async () => {
    const requests = mockFetch({ '/bff/auth/antiforgery': () => jsonResponse({ requestToken: '' }) });

    await caught(evaluatePosition(POSITION_ID));

    expect(requests.map((request) => request.url)).toEqual(['/bff/auth/antiforgery']);
  });
});
