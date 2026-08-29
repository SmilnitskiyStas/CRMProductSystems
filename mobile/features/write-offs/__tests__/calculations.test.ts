import { calculateReimbursement, money } from '../calculations';

describe('write-off calculations', () => {
  test('calculates a fixed reimbursement per written-off unit', () => {
    expect(calculateReimbursement(3, 20, 'fixed', 7.5)).toBe(22.5);
  });

  test('calculates a percentage from the purchase loss', () => {
    expect(calculateReimbursement(4, 25, 'percent', 30)).toBe(30);
  });

  test('returns zero when reimbursement is not configured', () => {
    expect(calculateReimbursement(2, 10, null, null)).toBe(0);
  });

  test('formats nullable currency values', () => {
    expect(money(12.5)).toBe('12.50 ₴');
    expect(money(null)).toBe('—');
  });
});
