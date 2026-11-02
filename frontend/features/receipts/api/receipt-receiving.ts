import { api, ApiError, getSessionEpoch } from "@/lib/api";
import type { ReceiptDto } from "../types";

export type ReceivingConfirmation = { tenantId: string; actorId: string; expectedFingerprint?: string | null; locationId: string };
type Context = { receiptId: string; locationId: string; fingerprint: string; configured: boolean; fenced: boolean; canReceive: boolean };
function key(id: string, who: ReceivingConfirmation) { return `sg_receipt_receiving:v1:${who.tenantId}:${who.actorId}:${id}`; }
function read(id: string, who: ReceivingConfirmation): string | null {
  try {
    const value = sessionStorage.getItem(key(id, who));
    if (value !== null && !/^[0-9a-f]{64}$/.test(value)) throw new Error("Malformed receiving confirmation");
    return value;
  } catch { throw new ApiError(503, "Cannot recover the saved receiving confirmation.", { code: "receipt_receiving_storage" }); }
}
export function hasPendingReceiving(id: string, who: ReceivingConfirmation): boolean { return read(id, who) !== null; }

/** Receipt ID is the permanent idempotency identity; retain the original reviewed fingerprint on an unknown outcome. */
export async function receiveReviewedReceipt(id: string, who: ReceivingConfirmation, legacy: () => Promise<ReceiptDto>): Promise<ReceiptDto> {
  const epoch = getSessionEpoch();
  const checkSession = () => { if (getSessionEpoch() !== epoch) throw new ApiError(401, "Session changed."); };
  const saved = read(id, who);
  if (!saved && !who.expectedFingerprint) return legacy(); // Old server DTO: DB fence still rejects legacy creation if activated.
  const context = await api.get<Context>(`/api/erp/receipts/${id}/receiving`);
  checkSession();
  if (context.receiptId !== id || context.locationId !== who.locationId)
    throw new ApiError(409, "Receipt scope changed.", { code: "receipt_posting_conflict" });
  if (!saved && !context.fenced && !context.configured) return legacy();
  if (!context.canReceive) throw new ApiError(409, "ERP receiving is unavailable.", { code: "inventory_posting_disabled" });
  const fingerprint = saved ?? who.expectedFingerprint!;
  if (!/^[0-9a-f]{64}$/.test(fingerprint) || !saved && context.fingerprint !== fingerprint)
    throw new ApiError(409, "Receipt changed since review.", { code: "receipt_posting_conflict" });
  try { sessionStorage.setItem(key(id, who), fingerprint); }
  catch { throw new ApiError(503, "Cannot save the receiving confirmation.", { code: "receipt_receiving_storage" }); }
  try {
    const result = await api.post<ReceiptDto>(`/api/erp/receipts/${id}/receive`, Object.freeze({ expectedFingerprint: fingerprint }));
    checkSession();
    if (result.id !== id || result.status !== "received" || !result.erpPostingId || !result.erpDocumentId)
      throw new Error("Unconfirmed ERP receiving response");
    try { sessionStorage.removeItem(key(id, who)); } catch { /* A remaining key safely replays the receipt. */ }
    return result;
  } catch (error) {
    if (!saved && error instanceof ApiError && [400, 403, 404, 409].includes(error.status)) {
      try { sessionStorage.removeItem(key(id, who)); } catch { /* Preserve the same fingerprint if storage is unavailable. */ }
    }
    throw error;
  }
}
