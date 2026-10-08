import { describe, expect, it } from 'vitest';
import { jsonResponse, mockFetch } from '../test/fetchMock';
import { PortfolioApiError, getPortfolio, listPositions, positionsUrl } from './portfolioApi';
import type { Portfolio, PositionListItem } from './portfolioTypes';

const ACCOUNT_ID = '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6';
const PORTFOLIO_URL = `/bff/me/exchange-accounts/${ACCOUNT_ID}/portfolio`;

const portfolio: Portfolio = {
  exchangeAccountId: ACCOUNT_ID,
  calculatedAt: '2026-10-07T10:00:00Z',
  capital: {
    totalEquity: 18420,
    availableCapital: 7310,
    totalWalletBalance: 18155,
    observedAt: '2026-10-07T10:00:00Z',
  },
  totalUnrealizedPnl: 265,
  positionsFullyReconciled: true,
  isComplete: true,
  isFresh: true,
  currentPositionCount: 3,
  exposures: [
    { settlementAsset: 'USDC', grossExposure: 5000, longExposure: 5000, shortExposure: 0 },
    { settlementAsset: 'USDT', grossExposure: 13000, longExposure: 10000, shortExposure: 3000 },
  ],
};

const position: PositionListItem = {
  id: '8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001',
  exchangeAccountId: ACCOUNT_ID,
  symbol: 'BTCUSDT',
  side: 'long',
  trackingState: 'active',
  size: 0.15,
  averageEntryPrice: 68500,
  markPrice: 70120,
  positionValue: 10518,
  unrealizedPnl: 230,
  leverage: 5,
  liquidationPrice: null,
  firstDetectedAt: '2026-10-01T10:00:00Z',
  lastObservedAt: '2026-10-07T10:00:00Z',
  closedAt: null,
  settlementAsset: 'USDT',
};

function problemResponse(status: number, body: Record<string, unknown>): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  });
}

describe('portfolioApi', () => {
  it('reads the portfolio through the same-origin BFF without caching', async () => {
    const requests = mockFetch({
      // Поля, которые UI не использует, допускаются: контракт развивается аддитивно.
      [PORTFOLIO_URL]: () => jsonResponse({ ...portfolio, grossExposure: null, netExposure: null }),
    });

    const result = await getPortfolio(ACCOUNT_ID);

    expect(result).toMatchObject(portfolio);
    expect(requests).toHaveLength(1);
    expect(requests[0]?.init?.credentials).toBe('same-origin');
    expect(requests[0]?.init?.cache).toBe('no-store');
    expect(requests[0]?.init?.method).toBeUndefined();
  });

  it('keeps multiple exposure groups separate without a cross-asset total', async () => {
    mockFetch({ [PORTFOLIO_URL]: () => jsonResponse(portfolio) });

    const result = await getPortfolio(ACCOUNT_ID);

    expect(result?.exposures.map((exposure) => exposure.settlementAsset)).toEqual(['USDC', 'USDT']);
    expect(result?.exposures[1]).toEqual({
      settlementAsset: 'USDT',
      grossExposure: 13000,
      longExposure: 10000,
      shortExposure: 3000,
    });
  });

  it('accepts unknown monetary values as null', async () => {
    const unknown = {
      ...portfolio,
      capital: { totalEquity: null, availableCapital: null, totalWalletBalance: null, observedAt: null },
      totalUnrealizedPnl: null,
      exposures: [{ settlementAsset: 'USDT', grossExposure: null, longExposure: 100, shortExposure: null }],
    };
    mockFetch({ [PORTFOLIO_URL]: () => jsonResponse(unknown) });

    await expect(getPortfolio(ACCOUNT_ID)).resolves.toMatchObject(unknown);
  });

  it('returns null when the portfolio has not been calculated yet', async () => {
    mockFetch({ [PORTFOLIO_URL]: () => new Response(null, { status: 204 }) });

    await expect(getPortfolio(ACCOUNT_ID)).resolves.toBeNull();
  });

  it('keeps only machine-readable ProblemDetails fields of a portfolio error', async () => {
    mockFetch({
      [PORTFOLIO_URL]: () =>
        problemResponse(404, {
          title: 'Not found',
          detail: 'Human readable text',
          status: 404,
          code: 'resource_not_found',
          traceId: '00-trace-01',
        }),
    });

    const error = (await getPortfolio(ACCOUNT_ID).catch((caught: unknown) => caught)) as PortfolioApiError;

    expect(error).toBeInstanceOf(PortfolioApiError);
    expect(error.status).toBe(404);
    expect(error.code).toBe('resource_not_found');
    expect(error.traceId).toBe('00-trace-01');
    expect(error.message).not.toContain('Human readable text');
  });

  it('reports a BFF status without body as an error without code', async () => {
    mockFetch({ [PORTFOLIO_URL]: () => new Response(null, { status: 401 }) });

    const error = (await getPortfolio(ACCOUNT_ID).catch((caught: unknown) => caught)) as PortfolioApiError;

    expect(error.status).toBe(401);
    expect(error.code).toBeNull();
  });

  it.each([
    ['missing exposures', { ...portfolio, exposures: undefined }],
    ['blank settlement asset', { ...portfolio, exposures: [{ ...portfolio.exposures[0], settlementAsset: ' ' }] }],
    ['string money', { ...portfolio, totalUnrealizedPnl: '265' }],
    ['negative position count', { ...portfolio, currentPositionCount: -1 }],
  ])('rejects a malformed portfolio: %s', async (_, body) => {
    mockFetch({ [PORTFOLIO_URL]: () => jsonResponse(body) });

    await expect(getPortfolio(ACCOUNT_ID)).rejects.toBeInstanceOf(PortfolioApiError);
  });

  it('encodes the account id as a single path segment', async () => {
    const requests = mockFetch({
      '/bff/me/exchange-accounts/..%2Fauth%2Fme/portfolio': () => new Response(null, { status: 404 }),
    });

    await expect(getPortfolio('../auth/me')).rejects.toBeInstanceOf(PortfolioApiError);
    expect(requests[0]?.url).toBe('/bff/me/exchange-accounts/..%2Fauth%2Fme/portfolio');
  });

  it('lists positions with settlement asset and cursor page fields', async () => {
    const usdc = { ...position, id: 'second', symbol: 'ETHUSDC', settlementAsset: 'USDC' };
    const url = `/bff/me/positions?exchangeAccountId=${ACCOUNT_ID}`;
    const requests = mockFetch({
      [url]: () => jsonResponse({ items: [position, usdc], nextCursor: 'opaque', hasMore: true }),
    });

    const page = await listPositions({ exchangeAccountId: ACCOUNT_ID });

    expect(page).toEqual({ items: [position, usdc], nextCursor: 'opaque', hasMore: true });
    expect(page.items.map((item) => item.settlementAsset)).toEqual(['USDT', 'USDC']);
    expect(requests[0]?.init?.credentials).toBe('same-origin');
    expect(requests[0]?.init?.cache).toBe('no-store');
  });

  it.each([
    ['missing settlement asset', { ...position, settlementAsset: undefined }],
    ['blank settlement asset', { ...position, settlementAsset: '' }],
    ['unknown side', { ...position, side: 'flat' }],
    ['unknown tracking state', { ...position, trackingState: 'critical' }],
  ])('rejects a malformed position: %s', async (_, item) => {
    mockFetch({
      [`/bff/me/positions?exchangeAccountId=${ACCOUNT_ID}`]: () =>
        jsonResponse({ items: [item], nextCursor: null, hasMore: false }),
    });

    await expect(listPositions({ exchangeAccountId: ACCOUNT_ID })).rejects.toBeInstanceOf(PortfolioApiError);
  });

  it('passes the opaque cursor through unchanged and URL-encoded', async () => {
    const cursor = 'eyJ0Ijoi+/=&symbol=ETHUSDT';
    const url = positionsUrl({ exchangeAccountId: ACCOUNT_ID, cursor });
    const requests = mockFetch({ [url]: () => jsonResponse({ items: [], nextCursor: null, hasMore: false }) });

    await listPositions({ exchangeAccountId: ACCOUNT_ID, cursor });

    const sent = new URL(requests[0]!.url, 'http://localhost');
    expect(sent.searchParams.get('cursor')).toBe(cursor);
    expect(sent.searchParams.getAll('symbol')).toEqual([]);
  });

  it('builds the positions query in a fixed order and omits unset filters', () => {
    expect(positionsUrl({ exchangeAccountId: ACCOUNT_ID })).toBe(`/bff/me/positions?exchangeAccountId=${ACCOUNT_ID}`);
    expect(
      positionsUrl({
        exchangeAccountId: ACCOUNT_ID,
        trackingState: 'closed',
        side: 'short',
        symbol: 'BTCUSDT',
        cursor: 'abc',
      }),
    ).toBe(`/bff/me/positions?exchangeAccountId=${ACCOUNT_ID}&trackingState=closed&side=short&symbol=BTCUSDT&cursor=abc`);
    expect(positionsUrl({ exchangeAccountId: ACCOUNT_ID, symbol: '' })).not.toContain('symbol');
  });

  it('reports a positions validation problem as a typed error', async () => {
    const url = positionsUrl({ exchangeAccountId: ACCOUNT_ID, cursor: 'broken' });
    mockFetch({ [url]: () => problemResponse(400, { status: 400, code: 'validation_failed', traceId: 't' }) });

    const error = (await listPositions({ exchangeAccountId: ACCOUNT_ID, cursor: 'broken' }).catch(
      (caught: unknown) => caught,
    )) as PortfolioApiError;

    expect(error).toBeInstanceOf(PortfolioApiError);
    expect(error.status).toBe(400);
    expect(error.code).toBe('validation_failed');
  });
});
