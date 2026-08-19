/**
 * Calculates the amount the customer still owes after loyalty redemption.
 * The server remains the source of truth; this helper keeps the cash/card UI
 * aligned with the value sent to the POS API.
 */
export function calculateNetTotal(subtotal: number, redeemAmount: number): number {
  const safeSubtotal = Number.isFinite(subtotal) ? Math.max(0, subtotal) : 0;
  const safeRedemption = Number.isFinite(redeemAmount) ? Math.max(0, redeemAmount) : 0;
  return Math.max(0, safeSubtotal - safeRedemption);
}
