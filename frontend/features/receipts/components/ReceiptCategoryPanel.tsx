"use client";
import styles from './ReceiptCategoryPanel.module.css';
import {useEffect,useMemo,useState} from 'react';
import {ReceiptLinePages} from './CategoryLinePages';
import type {ReceiptLineEdit,ReceiptSource,ReceiptWork,ReceiptWorkCommand,Result,User} from '../category-contracts';
const result=<T,>(promise:Promise<T>)=>promise;
import {receiptCategoryApi} from '../api/category-work';
export function ReceiptCategoryPanel({source,user,onChanged,onReview,onBlocked}:{source:ReceiptSource;user:User;onChanged:()=>void;onReview:()=>void;onBlocked?:(blocked:boolean)=>void}){
 const [work,setWork]=useState<ReceiptWork|null>(source.receivingWork??null),[key,setKey]=useState(''),[error,setError]=useState(''),[busy,setBusy]=useState(false);
 const [edits,setEdits]=useState<ReceiptLineEdit[]>([]),[dirty,setDirty]=useState(false),[checked,setChecked]=useState<Set<string>>(new Set()),[chosen,setChosen]=useState<Set<string>>(new Set());
 const [requestId,setRequestId]=useState(()=>crypto.randomUUID()),[unknown,setUnknown]=useState<ReceiptWorkCommand|null>(null);
 useEffect(()=>{let active=true;void result(receiptCategoryApi.get(source.id,source.destinationStoreId)).then(v=>{if(active){setWork(v);setKey(k=>v?.groups.some(g=>g.key===k)?k:v?.groups[0]?.key??'');}}).catch(e=>{if(active)setError(e.message);});return()=>{active=false;};},[source.id,source.receivingFingerprint,source.items]);
 useEffect(()=>{onBlocked?.(busy||dirty||!!unknown);},[busy,dirty,unknown,onBlocked]);
 const group=work?.groups.find(g=>g.key===key),ids=useMemo(()=>new Set(group?.lineIds??[]),[group]);
 const lines=useMemo(()=>source.items.filter(l=>ids.has(l.id)),[source.items,ids]);
 useEffect(()=>{setEdits(lines.map(l=>({itemId:l.id,quantityReceived:l.quantityReceived??l.quantityOrdered,pricePurchase:l.pricePurchase,expiryDate:l.expiryDate??'',batchNumber:l.batchNumber??'',discrepancyNotes:l.discrepancyNotes})));setDirty(false);setChecked(new Set());setChosen(new Set());setRequestId(crypto.randomUUID());setUnknown(null);},[key,source.receivingFingerprint,source.items]);
 const mine=group?.ownerId===user.id,editable=!!work?.open&&mine&&!group?.ready,canRelease=mine||['enterprise_admin','network_manager'].includes(user.role);
 async function change(command:ReceiptWorkCommand){setBusy(true);setError('');try{const next=await result(receiptCategoryApi.change(source.id,source.destinationStoreId,command));setWork(next);setDirty(false);setUnknown(null);setRequestId(crypto.randomUUID());if(command.action==='reopen'||command.action==='take'||command.action==='release')setChecked(new Set());if(command.action==='take'){const owned=next.groups.find(g=>g.ownerId===user.id&&g.lineIds.some(id=>chosen.has(id)));if(owned)setKey(owned.key);}onChanged();}catch(e){setError((e as Error).message);if(command.action==='save'&&!(typeof (e as {status?:number}).status==='number'&&(e as {status:number}).status>=400&&(e as {status:number}).status<500)&&!['money','validation','expiry','receipt_lines','receipt_item','receipt_empty','receipt_category_conflict'].includes((e as {code?:string}).code??''))setUnknown(command);}finally{setBusy(false);}}
 function update(id:string,patch:Partial<ReceiptLineEdit>){setEdits(old=>old.map(e=>e.itemId===id?{...e,...patch}:e));setDirty(true);setRequestId(crypto.randomUUID());setChecked(old=>{const next=new Set(old);next.delete(id);return next;});}
 if(!work)return error?<div className="error" role="alert">{error}</div>:null;
 return <section className={styles.root} aria-label="Спільне приймання за категоріями"><h3>Приймання за категоріями</h3>
 <p className="muted">Кожен менеджер бере свою категорію або вибрані товари. Товари без категорії потрапляють до «Інше». Надходження проводиться після завершення всіх груп.</p>
 {error&&<div className="error" role="alert">{error}</div>}
 {!work.active?work.open&&<button className="secondary" disabled={busy} onClick={()=>void change({action:'start'})}>Розподілити за категоріями</button>:<>
 <p role="status">Завершено груп: {work.groups.filter(g=>g.ready).length} з {work.groups.length}</p>
 <label>Категорія<select aria-label="Категорія приймання" disabled={busy||dirty||!!unknown} value={key} onChange={e=>setKey(e.target.value)}>{work.groups.map((g,i)=><option key={g.key} value={g.key}>{g.name} · {g.lineIds.length} товарів · {g.ownerName??(g.ownerId?'Менеджер':'Вільна')} · {g.ready?'Завершено':'Очікує'} · {i+1}</option>)}</select></label>
 {group&&<><p>Відповідальний: {group.ownerName??(group.ownerId?'Менеджер':'Немає')} {group.ready?'· Категорію завершено':''}</p>
 {work.open&&!group.ownerId&&<div className="actions"><button disabled={busy} className="primary" onClick={()=>void change({action:'take',key})}>Взяти категорію в роботу</button><button disabled={busy||chosen.size===0} className="secondary" onClick={()=>void change({action:'take',key,lineIds:[...chosen]})}>Взяти вибрані товари ({chosen.size})</button></div>}
 {work.open&&group.ownerId&&canRelease&&<button disabled={busy||dirty||!!unknown} className="secondary" onClick={()=>void change({action:'release',key})}>Звільнити групу для іншого менеджера</button>}
 {work.open&&mine&&group.ready&&<button className="secondary" disabled={busy||dirty||!!unknown} onClick={()=>void change({action:'reopen',key})}>Повернути категорію до перевірки</button>}
 <ReceiptLinePages key={key} items={lines} busy={busy||!!unknown} reviewed={checked} onReview={editable?values=>setChecked(old=>new Set([...old,...values])):undefined}>{rows=>rows.map(({line,index})=>{
 const edit=edits.find(e=>e.itemId===line.id);return <fieldset className="receipt-edit-line" key={line.id} data-category-line={line.id} disabled={busy||!!unknown}><legend>{index+1}. {line.productName}</legend>
 {!group.ownerId&&work.open&&<label><input type="checkbox" aria-label={`Вибрати ${line.productName}`} checked={chosen.has(line.id)} onChange={e=>setChosen(old=>{const next=new Set(old);e.target.checked?next.add(line.id):next.delete(line.id);return next;})}/>Взяти цей товар</label>}
 {editable&&edit?<div className="receipt-input-grid"><label>Фактично отримано<input aria-label={`Отримано: ${line.productName}`} inputMode="decimal" value={edit.quantityReceived} onChange={e=>update(line.id,{quantityReceived:e.target.value})}/></label><label>Ціна, грн<input aria-label={`Ціна: ${line.productName}`} inputMode="decimal" value={edit.pricePurchase??''} onChange={e=>update(line.id,{pricePurchase:e.target.value||null})}/></label><label>Придатний до<input aria-label={`Придатний до: ${line.productName}`} type="date" value={edit.expiryDate} onChange={e=>update(line.id,{expiryDate:e.target.value})}/></label><label>Партія<input aria-label={`Партія: ${line.productName}`} maxLength={100} value={edit.batchNumber} onChange={e=>update(line.id,{batchNumber:e.target.value})}/></label><label>Розбіжності<input aria-label={`Розбіжності: ${line.productName}`} maxLength={500} value={edit.discrepancyNotes??''} onChange={e=>update(line.id,{discrepancyNotes:e.target.value||null})}/></label></div>:<p>Замовлено {line.quantityOrdered} {line.unit} · Отримано {line.quantityReceived??'Не перевірено'} · Придатний до {line.expiryDate??'Не вказано'}</p>}</fieldset>;
 })}</ReceiptLinePages>
 {editable&&lines.length<=50&&<label className="confirmation"><input type="checkbox" checked={lines.length>0&&lines.every(l=>checked.has(l.id))} disabled={busy||!!unknown} onChange={e=>setChecked(e.target.checked?new Set(lines.map(l=>l.id)):new Set())}/>Перевірив усі товари цієї групи</label>}
 {editable&&<div className="actions"><button className="secondary" disabled={busy||!!unknown} onClick={()=>void change({action:'save',key,save:{requestId,expectedFingerprint:group.fingerprint,items:edits,complete:false}})}>Зберегти прогрес категорії</button><button className="primary" disabled={busy||!!unknown||!lines.length||!lines.every(l=>checked.has(l.id))} onClick={()=>void change({action:'save',key,save:{requestId,expectedFingerprint:group.fingerprint,items:edits,complete:true}})}>Завершити категорію</button></div>}
 {unknown&&<><p className="warning-note">Збереження не підтверджено. Можна повторити той самий запит або закрити накладну й перевірити збережений стан. Правки залишаються тут.</p><button disabled={busy} className="secondary" onClick={()=>void change(unknown)}>Перевірити тим самим запитом</button><button disabled={busy} className="text-button" onClick={()=>{setUnknown(null);setDirty(false);onChanged();}}>Перечитати збережений стан</button></>}
 {dirty&&<p className="warning-note">Є незбережені правки цієї категорії.</p>}
 </>}
 <button className="secondary" disabled={busy||dirty||!!unknown} onClick={onChanged}>Оновити стан категорій</button>
 {work.open&&work.groups.length>0&&work.groups.every(g=>g.ready)&&<button className="primary" disabled={busy||dirty||!!unknown||!user.twoFactorEnabled} onClick={onReview}>Перевірити всю накладну перед проведенням</button>}
 </>}
 </section>;
}
