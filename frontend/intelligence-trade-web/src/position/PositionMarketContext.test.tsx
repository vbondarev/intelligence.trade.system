import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { marketFixture } from '../test/positionFixtures';
import { PositionMarketContext } from './PositionMarketContext';

const normalize = (value: string) => value.replace(/\s/g, ' ');

function valueOf(term: string): string {
  return normalize(screen.getByText(term, { selector: 'dt' }).nextElementSibling?.textContent ?? '');
}

function renderWithPrice(price24hChangePct: number) {
  const market = marketFixture();
  render(<PositionMarketContext market={{ ...market, price: { ...market.price, price24hChangePct } }} />);
}

describe('PositionMarketContext', () => {
  it.each([
    [0.0234, '+2,34 %'],
    [-0.0156, '−1,56 %'],
    [0, '0,00 %'],
  ])('shows 24h price change fraction %s as %s', (price24hChangePct, expected) => {
    renderWithPrice(price24hChangePct);

    expect(valueOf('Изменение 24ч')).toBe(expected);
  });

  it('keeps metrics already expressed in percent units unscaled', () => {
    const market = marketFixture();
    render(
      <PositionMarketContext
        market={{
          ...market,
          price: { ...market.price, spreadPct: 0.25 },
          derivatives: { ...market.derivatives, premiumVsIndexPct: 0.05 },
        }}
      />,
    );

    expect(valueOf('Спред')).toBe('0,25 %');
    expect(valueOf('Премия к индексу')).toBe('0,05 %');
    expect(valueOf('Изменение OI 1ч / 4ч')).toBe('+0,40 % / −1,10 %');
    expect(valueOf('Поток сделок, дельта')).toBe('+9,09 %');
    expect(valueOf('Funding')).toBe('+0,01 %');
    expect(valueOf('Long / Short')).toBe('52 % / 48 %');
  });
});
