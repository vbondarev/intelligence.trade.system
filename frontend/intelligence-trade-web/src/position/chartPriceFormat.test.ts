import { expect, it } from 'vitest';
import { chartPriceScale, formatChartPrice } from './chartPriceFormat';

it.each([
  [42, '42,0000000000'],
  [42.13, '42,1300000000'],
  [1e-9, '0,0000000010'],
  [1e-20, '0,' + '0'.repeat(19) + '1'],
  [1e-28, '0,' + '0'.repeat(27) + '1'],
  [1.2345678901234567, '1,2345678901234567'],
])('сохраняет точность цены %s с минимумом десять дробных цифр', (value, expected) => {
  expect(formatChartPrice(value)).toBe(expected);
});

it('сохраняет обычный шаг шкалы и ограничивает шаг для очень малых цен', () => {
  expect(chartPriceScale([42, 42.13])).toBe(2);
  expect(chartPriceScale([42, 2.5e-9])).toBe(10);
  expect(chartPriceScale([1e-28])).toBe(20);
});
