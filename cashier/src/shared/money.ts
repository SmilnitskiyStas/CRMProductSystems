/** Money helpers: all arithmetic in integer kopecks to avoid float drift. */
export const toKop = (amount: number): number => Math.round(amount * 100);
export const fromKop = (kop: number): number => kop / 100;
export const formatMoney = (amount: number): string =>
  new Intl.NumberFormat('uk-UA', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(amount) + ' ₴';
