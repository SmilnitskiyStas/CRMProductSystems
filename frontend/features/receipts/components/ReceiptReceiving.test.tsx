import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { NextIntlClientProvider } from "next-intl";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import Page from "@/app/(dashboard)/receipts/[id]/page";
import { api, ApiError } from "@/lib/api";
import { receiptsApi } from "../api/receipts";
import uk from "@/messages/uk.json";
import type { ReceiptDto } from "../types";

const state=vi.hoisted(()=>({receipt:null as ReceiptDto|null,update:vi.fn(),receive:vi.fn(),reload:vi.fn(),push:vi.fn()}));
vi.mock("next/navigation",()=>({useParams:()=>({id:"receipt-a"}),useRouter:()=>({push:state.push})}));
vi.mock("@/features/auth/hooks/useAuth",()=>({useMe:()=>({data:{id:"actor-a",tenantId:"tenant-a",role:"storekeeper"}})}));
vi.mock("../hooks/useReceipts",()=>({useReceiptDetail:()=>({data:state.receipt,isLoading:false,refetch:state.reload}),
  useUpdateReceiptItems:()=>({mutateAsync:state.update,isPending:false}),useReceiveReceipt:()=>({mutateAsync:state.receive,isPending:false}),useCancelReceipt:()=>({mutate:vi.fn()})}));
const fp="a".repeat(64), nextFp="b".repeat(64);
const base:ReceiptDto={id:"receipt-a",supplierId:null,expectedAt:null,receivedAt:null,notes:null,createdAt:"2026-10-26T00:00:00Z",destinationStoreId:"store-a",destinationStoreName:"Магазин",supplierName:"Постачальник",viaCentralStore:false,status:"ordered",
  receivingFingerprint:fp,items:[{id:"line-a",productId:"item-a",productName:"Борошно",productBarcode:"123",unit:"кг",quantityOrdered:2.25,quantityReceived:2.25,
    expiryDate:"2027-01-01",batchNumber:"A-1",isProcessed:true,pricePurchase:12.5,discrepancyNotes:null}]};
const context={receiptId:base.id,locationId:base.destinationStoreId,fingerprint:fp,configured:true,fenced:true,canReceive:true};
const result:ReceiptDto={...base,status:"received",erpDocumentId:"doc",erpPostingId:"post"};
const t=uk.Dashboard.receipts.detail;
let container:HTMLDivElement,root:Root;
beforeEach(()=>{
  (globalThis as {IS_REACT_ACT_ENVIRONMENT?:boolean}).IS_REACT_ACT_ENVIRONMENT=true;
  vi.clearAllMocks(); sessionStorage.clear(); state.receipt=structuredClone(base); state.update.mockResolvedValue(state.receipt);
  state.receive.mockImplementation((x:{id:string;confirmation:Parameters<typeof receiptsApi.receive>[1]})=>receiptsApi.receive(x.id,x.confirmation));
  state.reload.mockImplementation(async()=>({data:state.receipt,error:null}));
  vi.spyOn(api,"get").mockResolvedValue(context);vi.spyOn(api,"post").mockResolvedValue(result);vi.spyOn(api,"put").mockResolvedValue(result);
  container=document.createElement("div");document.body.append(container);root=createRoot(container);
});
afterEach(()=>{act(()=>root.unmount());container.remove();vi.restoreAllMocks();});
function render(key=0){act(()=>root.render(<NextIntlClientProvider locale="uk" timeZone="Europe/Kyiv" messages={uk}><Page key={key}/></NextIntlClientProvider>));}
function button(text:string){return [...container.querySelectorAll("button")].find(x=>x.textContent?.trim()===text)!;}
async function click(text:string){await act(async()=>button(text).click());}

it("allows an ordered receipt to be received by its warehouse operator",async()=>{
  render();expect(button(t.confirmReceive).disabled).toBe(false);expect(container.textContent).toContain("2.25 кг");
  await click(t.confirmReceive);expect(api.post).toHaveBeenCalledWith("/api/erp/receipts/receipt-a/receive",{expectedFingerprint:fp});expect(state.push).toHaveBeenCalledWith("/receipts");
});
it("locks a receipt after an unknown outcome and resumes the same confirmation after reload",async()=>{
  vi.mocked(api.post).mockRejectedValueOnce(new TypeError("lost response"));render();await click(t.confirmReceive);
  expect(container.textContent).toContain(t.receivingUncertain);expect(button(uk.Common.cancel).disabled).toBe(true);
  state.receipt={...result,receivingFingerprint:nextFp};render(1);await click(t.retryReceive);
  expect(state.update).not.toHaveBeenCalled();expect(vi.mocked(api.post).mock.calls[0]).toEqual(vi.mocked(api.post).mock.calls[1]);
});
it("requires explicit review after the source fingerprint changes",async()=>{
  vi.mocked(api.get).mockResolvedValue({...context,fingerprint:nextFp});render();await click(t.confirmReceive);
  expect(api.post).not.toHaveBeenCalled();expect(button(t.reviewReceiving)).toBeTruthy();expect(button(t.confirmReceive).disabled).toBe(true);
  state.receipt={...base,receivingFingerprint:nextFp};await click(t.reviewReceiving);await click(t.confirmReceive);
  expect(api.post).toHaveBeenCalledWith("/api/erp/receipts/receipt-a/receive",{expectedFingerprint:nextFp});
});
it("prevents a double-click while the confirmation is in flight",async()=>{
  let finish!:(value:unknown)=>void;vi.mocked(api.post).mockImplementationOnce(()=>new Promise(resolve=>{finish=resolve;}));
  render();await click(t.confirmReceive);await click(t.confirmReceive);expect(api.post).toHaveBeenCalledOnce();
  expect(button(uk.Common.cancel).disabled).toBe(true);await act(async()=>finish(result));
});
it("does not post after item saving fails",async()=>{
  state.receipt={...base,items:[{...base.items[0],isProcessed:false}]};state.update.mockRejectedValue(new ApiError(400,"Bad quantity"));
  render();
  await click(t.confirmItem);expect(button(t.confirmReceive).disabled).toBe(false);await click(t.confirmReceive);
  expect(state.update).toHaveBeenCalledOnce();
  expect(api.post).not.toHaveBeenCalled();expect(state.push).not.toHaveBeenCalled();
});
it("receives the saved item fingerprint rather than the stale query snapshot",async()=>{
  state.receipt={...base,items:[{...base.items[0],isProcessed:false}]};state.update.mockResolvedValue({...base,receivingFingerprint:nextFp});
  vi.mocked(api.get).mockResolvedValue({...context,fingerprint:nextFp});render();await click(t.confirmItem);await click(t.confirmReceive);
  expect(state.update).toHaveBeenCalledOnce();expect(api.post).toHaveBeenCalledWith("/api/erp/receipts/receipt-a/receive",{expectedFingerprint:nextFp});
});

it("finalizes completed categories without writing the whole receipt over other managers edits",async()=>{
 const work={receiptId:base.id,locationId:base.destinationStoreId,active:true,open:true,groups:[{key:"milk",name:"Молочка",lineIds:["line-a"],ownerId:"another-manager",ownerName:"Інший менеджер",fingerprint:fp,ready:true,completedAt:null}]};
 state.receipt={...base,receivingWork:work};vi.mocked(api.get).mockImplementation(async(path:string)=>path.endsWith('/category-work')?work:context as any);
 render();expect(button(t.confirmReceive).disabled).toBe(false);await click(t.confirmReceive);expect(state.update).not.toHaveBeenCalled();expect(api.post).toHaveBeenCalledWith('/api/erp/receipts/receipt-a/receive',{expectedFingerprint:fp});
});
it("unfinished categories block final receiving even when every legacy line is processed",()=>{
 state.receipt={...base,receivingWork:{receiptId:base.id,locationId:base.destinationStoreId,active:true,open:true,groups:[{key:"milk",name:"Молочка",lineIds:["line-a"],ownerId:"actor-a",ownerName:null,fingerprint:fp,ready:false,completedAt:null}]}};
 render();expect(button(t.confirmReceive).disabled).toBe(true);expect(state.receive).not.toHaveBeenCalled();
});
it("bounds the web receipt table to fifty rows for a 201-line document",()=>{
 state.receipt={...base,items:Array.from({length:201},(_,i)=>({...base.items[0],id:'line-'+i,productName:'Великий товар '+i}))};render();expect(container.querySelectorAll('tbody tr')).toHaveLength(50);expect(container.textContent).toContain('Великий товар 0');expect(container.textContent).not.toContain('Великий товар 200');
});
