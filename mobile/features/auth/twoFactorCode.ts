export type TwoFactorCodeMode = 'totp' | 'recovery';

export function normalizeTwoFactorCode(
  value: string,
  mode: TwoFactorCodeMode
): string {
  if (mode === 'totp') return value.replace(/\D/g, '').slice(0, 6);

  const compact = value.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 8);
  return compact.length > 4 ? `${compact.slice(0, 4)}-${compact.slice(4)}` : compact;
}

export function isValidTwoFactorCode(
  value: string,
  mode: TwoFactorCodeMode
): boolean {
  return mode === 'totp'
    ? /^\d{6}$/.test(value)
    : /^[A-Z0-9]{4}-[A-Z0-9]{4}$/.test(value);
}
