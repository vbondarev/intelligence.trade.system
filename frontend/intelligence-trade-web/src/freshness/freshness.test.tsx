import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { MarketValuationDetails, ObservationDetails } from './FreshnessDetails';
import { factualFreshnessLabel, observationLabel, valuationFreshnessLabel, isFactualFreshness, isMarketValuation, isRealtimeObservation, isValuationFreshness } from './freshness';

describe('G-06 freshness', () => {
  it.each([
    ['fresh', 'Актуально'], ['stale', 'Устарело'], ['partial', 'Частично'],
    ['notApplicable', 'Не применяется'], ['unknown', 'Не подтверждено'],
  ])('renders valuation status %s', (status, expected) => {
    expect(valuationFreshnessLabel(status)).toBe(expected);
  });

  it('treats missing and future status values as unconfirmed', () => {
    expect(valuationFreshnessLabel(undefined)).toBe('Не подтверждено');
    expect(valuationFreshnessLabel('futureStatus')).toBe('Не подтверждено');
    expect(valuationFreshnessLabel('toString')).toBe('Не подтверждено');
    expect(factualFreshnessLabel('connected')).toBe('Не подтверждено');
    expect(observationLabel('fresh')).toBe('Не подтверждено');
    render(<ObservationDetails observation={{ status: 'futureStatus', lastConnectedAt: null, lastHeartbeatAt: null, reasonCode: null }} />);
    expect(screen.getByText('Не подтверждено')).toBeVisible();
  });

  it('validates nullable DTOs and rejects malformed values', () => {
    expect(isRealtimeObservation(null)).toBe(true);
    expect(isRealtimeObservation({ status: 'connected', lastConnectedAt: null, lastHeartbeatAt: 1, reasonCode: null })).toBe(false);
    expect(isFactualFreshness({ status: 'stale', lastFullyReconciledAt: null })).toBe(true);
    expect(isValuationFreshness({ status: 'partial', calculatedAt: false })).toBe(false);
    expect(isMarketValuation({ markPrice: '1' })).toBe(false);
  });

  it('keeps nulls and settlement currency separate from the persisted values', () => {
    const valuation = { markPrice: null, indicativePositionValue: 12.5, settlementAsset: 'USDC', source: null,
      observedAt: null, calculatedAt: '2026-10-01T00:00:00Z', freshnessStatus: 'stale' };
    expect(isMarketValuation(valuation)).toBe(true);
    expect(isMarketValuation({ ...valuation, settlementAsset: '  ' })).toBe(false);
    render(<MarketValuationDetails valuation={valuation} />);
    expect(screen.getByText('12,5 USDC')).toBeVisible();
    expect(screen.getByText('Устарело')).toBeVisible();
    expect(screen.getAllByText('—').length).toBeGreaterThan(0);
  });
});
