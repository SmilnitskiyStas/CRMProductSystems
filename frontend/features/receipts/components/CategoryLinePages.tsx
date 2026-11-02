"use client";
import {useEffect,useMemo,useRef,useState,type ReactNode} from 'react';
import type {ReceiptLine} from '../category-contracts';
const RECEIPT_PAGE_SIZE=50;
const empty=new Set<string>();
/** Pages bound DOM size only; the caller always owns the complete document and its edits. */
export function ReceiptLinePages({items,busy=false,reviewed=empty,onReview,children}:{items:ReceiptLine[];busy?:boolean;reviewed?:ReadonlySet<string>;onReview?:(ids:string[])=>void;children:(rows:{line:ReceiptLine;index:number}[])=>ReactNode}){
 const [page,setPage]=useState(1),[search,setSearch]=useState(''),pane=useRef<HTMLDivElement>(null);
 const indexed=useMemo(()=>items.map((line,index)=>({line,index})),[items]);
 const filtered=useMemo(()=>{const term=search.trim().toLocaleLowerCase('uk-UA');return term?indexed.filter(({line,index})=>`${index+1} ${line.productName} ${line.productBarcode??''} ${line.batchNumber??''}`.toLocaleLowerCase('uk-UA').includes(term)):indexed;},[indexed,search]);
 const pages=Math.max(1,Math.ceil(filtered.length/RECEIPT_PAGE_SIZE)),current=Math.min(page,pages),rows=filtered.slice((current-1)*RECEIPT_PAGE_SIZE,current*RECEIPT_PAGE_SIZE),large=items.length>RECEIPT_PAGE_SIZE;
 useEffect(()=>{setPage(1);setSearch('');},[items]);
 useEffect(()=>{if(pane.current)pane.current.scrollTop=0;},[current,search]);
 const checked=items.reduce((n,line)=>n+Number(reviewed.has(line.id)),0),pageChecked=rows.length>0&&rows.every(({line})=>reviewed.has(line.id));
 return <section className="receipt-line-pages" aria-label="Рядки накладної">
 {large&&<div className="receipt-page-controls"><label>Пошук рядків<input aria-label="Пошук рядків накладної" maxLength={200} value={search} disabled={busy} placeholder="Назва, штрихкод, партія або номер рядка" onChange={e=>{setSearch(e.target.value);setPage(1);}}/></label>
 <div className="actions"><button type="button" className="secondary" disabled={busy||current<=1} onClick={()=>setPage(p=>Math.max(1,p-1))}>Попередні рядки</button><label>Сторінка рядків<select aria-label="Сторінка рядків накладної" disabled={busy} value={current} onChange={e=>setPage(Number(e.target.value))}>{Array.from({length:pages},(_,i)=><option key={i+1} value={i+1}>{i+1} / {pages}</option>)}</select></label><button type="button" className="secondary" disabled={busy||current>=pages} onClick={()=>setPage(p=>p+1)}>Наступні рядки</button></div>
 <p className="muted" role="status">Показано {rows.length} із {filtered.length} знайдених рядків. У накладній: {items.length}.</p>
 {onReview&&<><p className="muted" data-testid="receipt-reviewed-count">Перевірено рядків: {checked} з {items.length}. Пошук і сторінки не виключають решту документа зі збереження.</p><button type="button" className="secondary" disabled={busy||rows.length===0||pageChecked} onClick={()=>onReview(rows.map(({line})=>line.id))}>{pageChecked?'Рядки сторінки перевірено':'Підтвердити рядки сторінки'}</button></>}
 </div>}
 <div ref={pane} className={large?'receipt-line-viewport':''} data-testid="receipt-visible-lines">{rows.length?children(rows):<p className="muted">Рядків за цим пошуком немає. Збережені правки інших рядків залишаються в документі.</p>}</div>
 </section>;
}
