import {
  isValidTwoFactorCode,
  normalizeTwoFactorCode,
} from '../twoFactorCode';

describe('two-factor code helpers', () => {
  test('normalizes TOTP input to six digits', () => {
    expect(normalizeTwoFactorCode('12a 34-567', 'totp')).toBe('123456');
    expect(isValidTwoFactorCode('123456', 'totp')).toBe(true);
    expect(isValidTwoFactorCode('12345', 'totp')).toBe(false);
  });

  test('normalizes and validates recovery codes', () => {
    expect(normalizeTwoFactorCode('ab1d ef2h', 'recovery')).toBe('AB1D-EF2H');
    expect(isValidTwoFactorCode('AB1D-EF2H', 'recovery')).toBe(true);
    expect(isValidTwoFactorCode('AB1D-EF2', 'recovery')).toBe(false);
  });
});
