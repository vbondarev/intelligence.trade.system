const numberFormat = new Intl.NumberFormat('ru-RU', { maximumSignificantDigits: 21 });
const minimumFractionDigits = 10;
const decimalSeparator = numberFormat.formatToParts(0.1).find((part) => part.type === 'decimal')!.value;

export function formatChartPrice(price: number): string {
  if (!Number.isFinite(price)) return numberFormat.format(price);
  const parts = numberFormat.formatToParts(price);
  const fraction = parts.find((part) => part.type === 'fraction');
  if (fraction) fraction.value = fraction.value.padEnd(minimumFractionDigits, '0');
  const formatted = parts.map((part) => part.value).join('');
  return fraction ? formatted : formatted + decimalSeparator + '0'.repeat(minimumFractionDigits);
}

export function chartPriceScale(prices: Iterable<number>): number {
  let scale = 2;
  for (const price of prices) {
    const [mantissa, exponent = '0'] = price.toString().split('e');
    const fractionLength = mantissa?.split('.')[1]?.length ?? 0;
    scale = Math.min(20, Math.max(scale, fractionLength - Number(exponent)));
  }
  return scale;
}
