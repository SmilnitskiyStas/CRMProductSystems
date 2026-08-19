import { calculateNetTotal } from '../calculateNetTotal';

describe('calculateNetTotal', () => {
  test('returns the subtotal when no loyalty balance is redeemed', () => {
    expect(calculateNetTotal(125.5, 0)).toBe(125.5);
  });

  test('subtracts loyalty redemption from the subtotal', () => {
    expect(calculateNetTotal(125.5, 25.25)).toBe(100.25);
  });

  test('never returns a negative amount', () => {
    expect(calculateNetTotal(25, 30)).toBe(0);
  });

  test('fails safely for negative and non-finite input', () => {
    expect(calculateNetTotal(-10, -5)).toBe(0);
    expect(calculateNetTotal(Number.NaN, 5)).toBe(0);
  });
});
