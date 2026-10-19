/**
 * Where a scanned code goes. Both scanners (USB wedge and camera) call the same entry point; while
 * the customer-scan mode is armed the NEXT code is a loyalty identifier and must never reach the
 * catalog lookup, otherwise it would show up as "barcode not found".
 */
export function routeScan<R>(
  customerArmed: boolean,
  code: string,
  handlers: { product: (code: string) => Promise<R>; customer: (code: string) => Promise<R> },
): Promise<R> {
  return customerArmed ? handlers.customer(code) : handlers.product(code);
}
