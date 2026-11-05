import {api,ApiError} from '@/lib/api';
import type {ReceiptWork,ReceiptWorkCommand,ReceiptSource} from '../category-contracts';
import type {ReceiptDto} from '../types';
export const receiptCategoryApi={
 async get(id:string,location:string):Promise<ReceiptWork|null>{try{const work=await api.get<ReceiptWork>(`/api/receipts/${id}/category-work`);if(work.receiptId!==id||work.locationId!==location||!Array.isArray(work.groups))throw new Error('Некоректний розподіл категорій.');return work;}catch(e){if(e instanceof ApiError&&e.status===404)return null;throw e;}},
 async change(id:string,location:string,command:ReceiptWorkCommand):Promise<ReceiptWork>{
  const body={...command,save:command.save?{...command.save,items:command.save.items.map(e=>({...e,quantityReceived:decimal(e.quantityReceived,8),pricePurchase:e.pricePurchase===null?null:decimal(e.pricePurchase,10),expiryDate:e.expiryDate||null}))}:undefined};
  const work=await api.post<ReceiptWork>(`/api/receipts/${id}/category-work`,body);
  if(work.receiptId!==id||work.locationId!==location)throw new Error('Категорії належать іншій локації.');return work;
 }
};
function decimal(value:string,digits:number){if(!new RegExp(`^\\d{1,${digits}}([.,]\\d{1,2})?$`).test(value.trim()))throw new Error('Перевірте кількість та ціну: до двох десяткових знаків.');return Number(value.replace(',','.'));}
export function categorySource(r:ReceiptDto):ReceiptSource{return{...r,source:r.source??'manual',takenByUserId:r.takenByUserId??null,receivingFingerprint:r.receivingFingerprint??'',erpDocumentId:r.erpDocumentId??null,erpPostingId:r.erpPostingId??null,erpReplayed:r.erpReplayed??false,receivingWork:r.receivingWork??undefined,items:r.items.map(l=>({...l,unit:l.unit??null,quantityOrdered:String(l.quantityOrdered),quantityReceived:l.quantityReceived===null?null:String(l.quantityReceived),pricePurchase:l.pricePurchase===null?null:String(l.pricePurchase)}))};}
