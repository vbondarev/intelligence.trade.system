import type {
  MarketCandle,
  MarketTimeframe,
  PositionCandles,
  PositionDetails,
  PositionEvaluation,
  PositionMarket,
  PositionTimelineEvaluation,
  PositionTimelineItem,
  PositionTimelinePage,
  PositionTimelinePositionChange,
  PositionTimelinePositionSnapshot,
  PositionTimelineRecommendation,
} from '../position/positionTypes';

export const POSITION_ID = '8b0f0d4c-7d33-4f4f-9b55-2d36c8f1c001';
export const OTHER_POSITION_ID = '5c1e2d3f-4a5b-4c6d-8e7f-901234567890';
export const ACCOUNT_ID = '2f6f4e0a-9b0b-4a3b-8db2-07e3c4b1d9a6';

export function positionFixture(overrides: Partial<PositionDetails> = {}): PositionDetails {
  return {
    id: POSITION_ID,
    exchangeAccountId: ACCOUNT_ID,
    symbol: 'BTCUSDT',
    side: 'long',
    trackingState: 'active',
    size: 0.5,
    averageEntryPrice: 60000,
    markPrice: 61000,
    positionValue: 30500,
    unrealizedPnl: 500,
    leverage: 5,
    liquidationPrice: 50000,
    firstDetectedAt: '2026-10-10T08:00:00Z',
    lastObservedAt: '2026-10-12T09:00:00Z',
    closedAt: null,
    marketCategory: 'linear',
    breakEvenPrice: 60100,
    takeProfit: 65000,
    stopLoss: 58000,
    trailingStop: null,
    settlementAsset: 'USDT',
    ...overrides,
  };
}

export function candleFixture(openTimeUtc: string, close = 61000): MarketCandle {
  return {
    openTimeUtc,
    open: close - 100,
    high: close + 200,
    low: close - 300,
    close,
    volume: 120.5,
    turnover: 7_300_000,
  };
}

function timeframeFixture(overrides: Partial<MarketTimeframe> = {}): MarketTimeframe {
  return {
    lastCandle: candleFixture('2026-10-12T08:00:00Z'),
    ema20: 60800,
    ema50: 60200,
    ema200: null,
    rsi14: 58.4,
    atr14: 420,
    volumeRatio: 1.2,
    trend: 'bullish',
    trendStrength: 0.7,
    support1: 59500,
    support2: 58200,
    resistance1: 62500,
    resistance2: 64000,
    distanceToSupport1Pct: 2.46,
    distanceToResistance1Pct: 2.46,
    rsi14IsReliable: true,
    emaIsReliable: true,
    emaHasFallback: false,
    atrIsReliable: true,
    atrIsFallback: false,
    volumeRatioIsReliable: true,
    volumeRatioIsFallback: false,
    ...overrides,
  };
}

export function marketFixture(overrides: Partial<PositionMarket> = {}): PositionMarket {
  return {
    positionId: POSITION_ID,
    exchange: 'bybit',
    symbol: 'BTCUSDT',
    marketCategory: 'linear',
    capturedAt: '2026-10-12T09:05:00Z',
    price: {
      lastPrice: 61000,
      markPrice: 61010,
      indexPrice: 61005,
      bidPrice: 60999,
      askPrice: 61001,
      spreadAbs: 2,
      spreadPct: 0.0033,
      price24hChangePct: -0.0125,
      high24h: 62000,
      low24h: 59800,
      volume24h: 15_000,
      turnover24h: 915_000_000,
    },
    derivatives: {
      fundingRate: 0.0001,
      nextFundingTime: '2026-10-12T16:00:00Z',
      openInterest: 52_000,
      openInterestValue: 3_172_000_000,
      openInterestChange1hPct: 0.4,
      openInterestChange4hPct: -1.1,
      longRatio: 0.52,
      shortRatio: 0.48,
      premiumVsIndexPct: null,
    },
    orderBook: {
      capturedAt: '2026-10-12T09:05:00Z',
      bestBidPrice: 60999,
      bestAskPrice: 61001,
      imbalanceTop5: 0.12,
      imbalanceTop10: 0.08,
      imbalanceTop20: -0.02,
    },
    tradeFlow: {
      windowStart: '2026-10-12T09:00:00Z',
      windowEnd: '2026-10-12T09:05:00Z',
      buyVolume: 120,
      sellVolume: 100,
      deltaVolume: 20,
      deltaPct: 9.09,
      totalTrades: 1500,
      hasAggressiveBuyPressure: true,
      hasAggressiveSellPressure: false,
    },
    m15: timeframeFixture({ trend: 'sideways' }),
    h1: timeframeFixture(),
    h4: timeframeFixture({ trend: 'bearish', rsi14: null, rsi14IsReliable: false }),
    d1: timeframeFixture({ trend: 'unknown' }),
    sentiment: {
      marketRegime: 'trending',
      longShortBiasScore: 0.1,
      fundingBiasScore: -0.05,
      orderBookPressureScore: 0.2,
      tradeFlowPressureScore: 0.3,
    },
    tags: ['trend-up'],
    ...overrides,
  };
}

export function candlesFixture(interval = '1h', items?: MarketCandle[]): PositionCandles {
  return {
    positionId: POSITION_ID,
    exchange: 'bybit',
    symbol: 'BTCUSDT',
    marketCategory: 'linear',
    interval,
    items: items ?? [
      candleFixture('2026-10-12T06:00:00Z', 60500),
      candleFixture('2026-10-12T07:00:00Z', 60800),
      candleFixture('2026-10-12T08:00:00Z', 61000),
    ],
  };
}

export function evaluationFixture(overrides: Partial<PositionEvaluation> = {}): PositionEvaluation {
  return {
    positionId: POSITION_ID,
    assessment: {
      id: 'a1b2c3d4-0000-4000-8000-000000000001',
      evaluatedAt: '2026-10-12T09:01:00Z',
      validUntil: '2999-01-01T00:00:00Z',
      ruleVersion: 'v1',
      isLegacy: false,
      inputIdentity: {
        positionId: POSITION_ID,
        exchangeAccountId: ACCOUNT_ID,
        instrumentId: 'bybit:linear:BTCUSDT',
        positionObservedAt: '2026-10-12T09:00:00Z',
        portfolioCalculatedAt: '2026-10-12T09:00:00Z',
        marketCapturedAt: '2026-10-12T09:00:30Z',
      },
      basePolicyIdentity: { version: 'base-1', hash: 'hash-base' },
      effectiveConfigurationIdentity: { version: 'cfg-1', hash: 'hash-cfg' },
      portfolioRiskDecision: 'allowed',
      reasonCodes: ['riskWithinLimits'],
      dataQuality: {
        market: 'freshCompleteReliable',
        portfolio: 'freshCompleteReliable',
        overall: 'freshCompleteReliable',
        safetyState: 'allowed',
      },
      result: {
        positionSide: 'long',
        currentPrice: 61000,
        trend: { marketTrend: 'bullish', positionAlignment: 'aligned', strength: 0.7, timeframe: '1h' },
        momentum: { rsi14: 58.4, isReliable: true, state: 'normal', potentialExhaustion: false },
        volatility: { atr14: 420, atrPercentOfPrice: 0.69, isReliable: true, isFallback: false },
        levels: {
          currentPrice: 61000,
          support1: 59500,
          distanceToSupport1Percent: 2.46,
          support1Strength: 0.6,
          resistance1: 62500,
          distanceToResistance1Percent: 2.46,
          resistance1Strength: 0.5,
        },
        pnl: {
          unrealizedPnl: 500,
          pnlPercent: 1.67,
          averageEntryPrice: 60000,
          currentPrice: 61000,
          priceRelativeToEntry: 'above',
          isFavorable: true,
        },
        stop: {
          stopPrice: 58000,
          distanceFromCurrentPercent: 4.92,
          stopRelativeToEntryPercent: -3.33,
          state: 'protective',
          priceRelativeToEntry: 'below',
          trailingStopDistance: null,
          hasTrailingStop: false,
        },
        breakeven: {
          breakEvenPrice: 60100,
          distanceFromCurrentPercent: 1.48,
          distanceFromEntryPercent: 0.17,
          priceRelativeToBreakEven: 'above',
          isProfitable: true,
        },
        liquidation: { liquidationPrice: 50000, distanceFromCurrentPercent: 18.03, state: 'far' },
        portfolioRisk: {
          policyDecision: 'allowed',
          freeCapitalPercent: 60,
          grossExposureToEquityPercent: 120,
          largestPositionConcentrationPercent: 40,
          totalUnrealizedPnl: 500,
          usedCapital: 4000,
          isComplete: true,
          isFresh: true,
          totalEquity: 10000,
          availableCapital: 6000,
          currentPositionValue: 30500,
          currentPositionConcentrationPercent: 40,
          minimumFreeCapitalPercent: 20,
          maximumGrossExposureToEquityPercent: 300,
          maximumPositionConcentrationPercent: 50,
        },
      },
    },
    recommendation: {
      id: 'r1b2c3d4-0000-4000-8000-000000000001',
      assessmentId: 'a1b2c3d4-0000-4000-8000-000000000001',
      createdAt: '2026-10-12T09:01:00Z',
      validUntil: '2999-01-01T00:00:00Z',
      status: 'active',
      policyIdentity: { version: 'policy-1', hash: 'hash-policy' },
      action: { value: 'hold', confidence: 0.7, priority: 'normal', reasonCodes: ['trendAligned', 'stopProtective'] },
      addDecision: {
        value: 'doNotAdd',
        reasonCodes: ['addBlockedByAction'],
        maximumAdditionalPositionValue: null,
        maximumAdditionalQuantity: null,
        conditions: null,
      },
      reasonCodes: ['trendAligned'],
      continuation: {
        nextEvaluationAt: '2026-10-12T13:01:00Z',
        invalidationConditions: [{ scope: 'action', kind: 'trendAlignment' }],
        reevaluationConditions: [{ scope: 'recommendation', kind: 'recommendationExpiry' }],
      },
    },
    ...overrides,
  };
}

export function timelineSnapshotFixture(
  overrides: Partial<PositionTimelinePositionSnapshot> = {},
): PositionTimelinePositionSnapshot {
  return {
    size: 0.3,
    averageEntryPrice: 60000,
    positionValue: 18300,
    leverage: 5,
    markPrice: 61000,
    breakEvenPrice: 60100,
    liquidationPrice: 50000,
    unrealizedPnl: 300,
    takeProfit: 65000,
    stopLoss: 58000,
    trailingStop: null,
    ...overrides,
  };
}

export function positionChangeItem(
  occurredAt: string,
  overrides: Partial<PositionTimelinePositionChange> = {},
): PositionTimelineItem {
  return {
    type: 'positionChange',
    occurredAt,
    positionChange: {
      sequence: 1,
      kind: 'updated',
      cause: 'exchangeObservation',
      trackingStateAfter: 'active',
      before: timelineSnapshotFixture(),
      after: timelineSnapshotFixture(),
      ...overrides,
    },
    evaluation: null,
    recommendation: null,
  };
}

export function timelineEvaluationItem(
  occurredAt: string,
  overrides: Partial<PositionTimelineEvaluation> = {},
): PositionTimelineItem {
  return {
    type: 'evaluation',
    occurredAt,
    positionChange: null,
    evaluation: {
      id: 'e0000000-0000-4000-8000-000000000001',
      evaluatedAt: occurredAt,
      validUntil: '2026-10-11T10:05:00Z',
      ruleVersion: 'assessment-v1',
      isLegacy: false,
      dataQuality: {
        market: 'freshCompleteReliable',
        portfolio: 'stale',
        overall: 'stale',
        safetyState: 'blocked',
      },
      portfolioRiskDecision: 'blocked',
      reasonCodes: ['portfolioDataStale', 'futureReasonCode'],
      ...overrides,
    },
    recommendation: null,
  };
}

export function timelineRecommendationItem(
  occurredAt: string,
  overrides: Partial<PositionTimelineRecommendation> = {},
): PositionTimelineItem {
  return {
    type: 'recommendation',
    occurredAt,
    positionChange: null,
    evaluation: null,
    recommendation: {
      id: 'c0000000-0000-4000-8000-000000000001',
      assessmentId: 'e0000000-0000-4000-8000-000000000001',
      createdAt: occurredAt,
      validUntil: '2026-10-11T14:00:00Z',
      status: 'superseded',
      action: 'watch',
      confidence: null,
      priority: null,
      addDecision: 'doNotAdd',
      reasonCodes: ['recommendationLimitedByDataQuality'],
      isLegacy: false,
      ...overrides,
    },
  };
}

/** Начальное наблюдение: прежнего состояния нет. */
export const NEW_POSITION_CHANGE = positionChangeItem('2026-10-10T08:00:00Z', {
  sequence: 1,
  kind: 'new',
  cause: 'initialObservation',
  before: null,
  after: timelineSnapshotFixture({ size: 0.2, positionValue: 12000, unrealizedPnl: 0 }),
});

export const INCREASED_POSITION_CHANGE = positionChangeItem('2026-10-11T09:00:00Z', {
  sequence: 2,
  kind: 'increased',
  before: timelineSnapshotFixture({ size: 0.2, positionValue: 12000, unrealizedPnl: 0 }),
  after: timelineSnapshotFixture({ size: 0.3 }),
});

export const REDUCED_POSITION_CHANGE = positionChangeItem('2026-10-12T07:00:00Z', {
  sequence: 3,
  kind: 'reduced',
  before: timelineSnapshotFixture({ size: 0.3 }),
  after: timelineSnapshotFixture({ size: 0.1, positionValue: 6100, unrealizedPnl: -15.5 }),
});

export const CLOSED_POSITION_CHANGE = positionChangeItem('2026-10-12T08:30:00Z', {
  sequence: 4,
  kind: 'closed',
  cause: 'missingFromCompleteObservation',
  trackingStateAfter: 'closed',
  before: timelineSnapshotFixture({ size: 0.1, positionValue: 6100, unrealizedPnl: -15.5 }),
  after: timelineSnapshotFixture({
    size: 0,
    averageEntryPrice: null,
    positionValue: null,
    unrealizedPnl: null,
    leverage: null,
    markPrice: null,
    breakEvenPrice: null,
    liquidationPrice: null,
    takeProfit: null,
    stopLoss: null,
  }),
});

export const TIMELINE_EVALUATION = timelineEvaluationItem('2026-10-12T07:30:00Z');

export const TIMELINE_RECOMMENDATION = timelineRecommendationItem('2026-10-12T07:30:00Z');

export function timelinePageFixture(
  items: PositionTimelineItem[],
  nextCursor: string | null = null,
): PositionTimelinePage {
  return { items, nextCursor, hasMore: nextCursor !== null };
}

/** Первая страница с тремя типами событий в порядке API (newest-first). */
export function firstTimelinePage(nextCursor: string | null = 'cursor+page/2=='): PositionTimelinePage {
  return timelinePageFixture([TIMELINE_RECOMMENDATION, TIMELINE_EVALUATION, REDUCED_POSITION_CHANGE], nextCursor);
}

export function continuationTimelinePage(nextCursor: string | null = null): PositionTimelinePage {
  return timelinePageFixture([INCREASED_POSITION_CHANGE, NEW_POSITION_CHANGE], nextCursor);
}

export function emptyTimelinePage(): PositionTimelinePage {
  return timelinePageFixture([]);
}
