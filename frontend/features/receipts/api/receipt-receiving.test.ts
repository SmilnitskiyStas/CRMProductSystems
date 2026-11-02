import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { api, ApiError, markLoggedOut } from "@/lib/api";
import { receiptsApi } from "./receipts";
import { hasPendingReceiving } from "./receipt-receiving";

const id="33333333-3333-4333-8333-333333333333", fingerprint="a".repeat(64);
const who={tenantId:"tenant-a",actorId:"actor-a",locationId:"store-a",expectedFingerprint:fingerprint};
const context={receiptId:id,locationId:who.locationId,fingerprint,configured:true,fenced:true,canReceive:true};
const result={id,status:"received",erpDocumentId:"document",erpPostingId:"posting"};
beforeEach(()=>{ sessionStorage.clear(); vi.spyOn(api,"get").mockResolvedValue(context); vi.spyOn(api,"post").mockResolvedValue(result); vi.spyOn(api,"put").mockResolvedValue(result); });
afterEach(()=>{ vi.restoreAllMocks(); });

it("uses the new posting endpoint only after confirming the displayed fingerprint and persists before POST",async()=>{
  vi.mocked(api.post).mockImplementationOnce(async()=>{expect(hasPendingReceiving(id,who)).toBe(true);return result as never;});
  await receiptsApi.receive(id,who);
  expect(api.post).toHaveBeenCalledWith(`/api/erp/receipts/${id}/receive`,{expectedFingerprint:fingerprint});
  expect(api.put).not.toHaveBeenCalled(); expect(hasPendingReceiving(id,who)).toBe(false);
});
it("preserves legacy receiving only on an explicit unfenced unconfigured scope",async()=>{
  vi.mocked(api.get).mockResolvedValue({...context,configured:false,fenced:false,canReceive:false}); await receiptsApi.receive(id,who);
  expect(api.put).toHaveBeenCalledWith(`/api/receipts/${id}/receive`); expect(api.post).not.toHaveBeenCalled();
});
it.each([{configured:false,fenced:true},{configured:true,fenced:false},{configured:true,fenced:true}])("never falls back when ERP is unavailable: %j",async flags=>{
  vi.mocked(api.get).mockResolvedValue({...context,...flags,canReceive:false}); await expect(receiptsApi.receive(id,who)).rejects.toBeInstanceOf(ApiError);
  expect(api.put).not.toHaveBeenCalled(); expect(api.post).not.toHaveBeenCalled();
});
it("does not fall back after a missing preview endpoint or network failure",async()=>{
  vi.mocked(api.get).mockRejectedValue(new ApiError(404,"Missing")); await expect(receiptsApi.receive(id,who)).rejects.toThrow(); expect(api.put).not.toHaveBeenCalled();
});
it("does not accept an unseen edit or another location",async()=>{
  vi.mocked(api.get).mockResolvedValueOnce({...context,fingerprint:"b".repeat(64)}).mockResolvedValueOnce({...context,locationId:"other"});
  await expect(receiptsApi.receive(id,who)).rejects.toThrow(); await expect(receiptsApi.receive(id,who)).rejects.toThrow(); expect(api.post).not.toHaveBeenCalled();
});
it("retries the original saved fingerprint after an unknown result even when the DTO changed",async()=>{
  vi.mocked(api.post).mockRejectedValueOnce(new TypeError("network")); await expect(receiptsApi.receive(id,who)).rejects.toThrow();
  vi.mocked(api.get).mockResolvedValue({...context,fingerprint:"b".repeat(64)});
  await receiptsApi.receive(id,{...who,expectedFingerprint:"b".repeat(64)});
  expect(vi.mocked(api.post).mock.calls[0]).toEqual(vi.mocked(api.post).mock.calls[1]); expect(api.put).not.toHaveBeenCalled();
});
it("does not erase an unknown outcome on a later 409",async()=>{
  vi.mocked(api.post).mockRejectedValueOnce(new TypeError("network")).mockRejectedValueOnce(new ApiError(409,"Conflict"));
  await expect(receiptsApi.receive(id,who)).rejects.toThrow(); await expect(receiptsApi.receive(id,who)).rejects.toThrow(); expect(hasPendingReceiving(id,who)).toBe(true);
});
it("does not return to legacy receiving when an earlier ERP outcome is unknown",async()=>{
  vi.mocked(api.post).mockRejectedValueOnce(new Error("unknown")); await expect(receiptsApi.receive(id,who)).rejects.toThrow();
  vi.mocked(api.get).mockResolvedValue({...context,configured:false,fenced:false,canReceive:false});
  await expect(receiptsApi.receive(id,{...who,expectedFingerprint:undefined})).rejects.toThrow(); expect(api.put).not.toHaveBeenCalled(); expect(hasPendingReceiving(id,who)).toBe(true);
});
it("releases only a definite first rejection for explicit fresh review",async()=>{
  vi.mocked(api.post).mockRejectedValueOnce(new ApiError(409,"Changed")); await expect(receiptsApi.receive(id,who)).rejects.toThrow();
  expect(hasPendingReceiving(id,who)).toBe(false);
});
it("sends nothing if the confirmation cannot be persisted",async()=>{
  vi.spyOn(Storage.prototype,"setItem").mockImplementation(()=>{throw new Error("quota");}); await expect(receiptsApi.receive(id,who)).rejects.toThrow();
  expect(api.post).not.toHaveBeenCalled(); expect(api.put).not.toHaveBeenCalled();
});
it("retains a pending confirmation for an invalid success body",async()=>{
  vi.mocked(api.post).mockResolvedValue({...result,id:"wrong"}); await expect(receiptsApi.receive(id,who)).rejects.toThrow(); expect(hasPendingReceiving(id,who)).toBe(true);
});
it("isolates saved receiving confirmations by tenant and actor",async()=>{
  vi.mocked(api.post).mockRejectedValueOnce(new Error("unknown")); await expect(receiptsApi.receive(id,who)).rejects.toThrow();
  expect(hasPendingReceiving(id,{...who,actorId:"other"})).toBe(false); expect(hasPendingReceiving(id,{...who,tenantId:"other"})).toBe(false); expect(hasPendingReceiving(id,who)).toBe(true);
});
it("retains the original user's confirmation when the session changes during the POST",async()=>{
  vi.mocked(api.post).mockImplementationOnce(async()=>{markLoggedOut();return result as never;}); await expect(receiptsApi.receive(id,who)).rejects.toThrow(); expect(hasPendingReceiving(id,who)).toBe(true);
});
it("blocks sending when the session changes during preview",async()=>{
  vi.mocked(api.get).mockImplementationOnce(async()=>{markLoggedOut();return context as never;}); await expect(receiptsApi.receive(id,who)).rejects.toThrow(); expect(api.post).not.toHaveBeenCalled();
});
it("keeps an old-server DTO compatible when there is no pending ERP command",async()=>{
  await receiptsApi.receive(id,{...who,expectedFingerprint:undefined}); expect(api.get).not.toHaveBeenCalled(); expect(api.put).toHaveBeenCalledOnce();
});
it("does not discard malformed saved data",async()=>{
  sessionStorage.setItem(`sg_receipt_receiving:v1:${who.tenantId}:${who.actorId}:${id}`,"corrupt"); await expect(receiptsApi.receive(id,who)).rejects.toThrow();
  expect(api.post).not.toHaveBeenCalled(); expect(api.put).not.toHaveBeenCalled();
});
