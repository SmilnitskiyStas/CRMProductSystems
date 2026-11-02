import {act} from 'react';
import {createRoot,type Root} from 'react-dom/client';
import {beforeEach,afterEach,it,expect,vi} from 'vitest';
import {ReceiptCategoryPanel} from './ReceiptCategoryPanel';
import type {ReceiptSource,ReceiptWork,ReceiptWorkCommand} from '../category-contracts';
const mock=vi.hoisted(()=>({get:vi.fn(),change:vi.fn()}));
vi.mock('../api/category-work',()=>({receiptCategoryApi:mock}));
let container:HTMLDivElement,root:Root,source:ReceiptSource,work:ReceiptWork;const changed=vi.fn();
beforeEach(()=>{(globalThis as {IS_REACT_ACT_ENVIRONMENT?:boolean}).IS_REACT_ACT_ENVIRONMENT=true;vi.clearAllMocks();
 const items=Array.from({length:201},(_,i)=>({id:'line-'+i,productId:'product-'+i,productName:'Товар '+i,productBarcode:null,unit:'шт',quantityOrdered:'1.00',quantityReceived:null,pricePurchase:'10.00',expiryDate:'2027-01-01',batchNumber:'B',discrepancyNotes:null}));
 work={receiptId:'receipt',locationId:'store',active:true,open:true,groups:[{key:'milk',name:'Молочка',lineIds:items.slice(0,151).map(l=>l.id),ownerId:'actor',ownerName:'Менеджер',fingerprint:'a'.repeat(64),ready:false,completedAt:null},{key:'other',name:'Інше',lineIds:items.slice(151).map(l=>l.id),ownerId:null,ownerName:null,fingerprint:'b'.repeat(64),ready:false,completedAt:null}]};
 source={id:'receipt',destinationStoreId:'store',destinationStoreName:'Магазин',supplierId:null,supplierName:null,status:'ordered',viaCentralStore:false,expectedAt:null,notes:null,source:'manual',takenByUserId:null,receivingFingerprint:'c'.repeat(64),items,receivedAt:null,erpDocumentId:null,erpPostingId:null,erpReplayed:false,receivingWork:work};
 mock.get.mockImplementation(async()=>structuredClone(work));mock.change.mockImplementation(async()=>structuredClone(work));container=document.createElement('div');document.body.append(container);root=createRoot(container);
});
afterEach(()=>{act(()=>root.unmount());container.remove();});
async function render(){await act(async()=>root.render(<ReceiptCategoryPanel source={source} user={{id:'actor',role:'storekeeper',twoFactorEnabled:true}} onChanged={changed} onReview={()=>{}}/>));}
function button(text:string){return [...container.querySelectorAll('button')].find(x=>x.textContent?.trim()===text)!;}
async function click(text:string){await act(async()=>button(text).click());}
async function select(label:string,value:string){const field=container.querySelector(`[aria-label="${label}"]`) as HTMLSelectElement;await act(async()=>{field.value=value;field.dispatchEvent(new Event('change',{bubbles:true}));});}
async function fill(label:string,value:string){const field=container.querySelector(`[aria-label="${label}"]`) as HTMLInputElement;await act(async()=>{Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value')!.set!.call(field,value);field.dispatchEvent(new Event('input',{bubbles:true}));});}
it('paginates a 151-line category, retains first and last edits and requires review of every page',async()=>{
 await render();expect(container.querySelectorAll('[data-category-line]')).toHaveLength(50);expect(button('Завершити категорію').disabled).toBe(true);
 await fill('Отримано: Товар 0','2.25');await select('Сторінка рядків накладної','4');expect(container.querySelectorAll('[data-category-line]')).toHaveLength(1);await fill('Отримано: Товар 150','0.75');
 for(let i=1;i<=4;i++){await select('Сторінка рядків накладної',String(i));await click('Підтвердити рядки сторінки');}
 expect(button('Завершити категорію').disabled).toBe(false);await click('Завершити категорію');const command=mock.change.mock.calls[0][2] as ReceiptWorkCommand;expect(command.save!.items).toHaveLength(151);expect(command.save!.items[0].quantityReceived).toBe('2.25');expect(command.save!.items[150].quantityReceived).toBe('0.75');expect(command.save!.complete).toBe(true);expect(changed).toHaveBeenCalledOnce();
});
it('allows selected uncategorized goods to be claimed separately',async()=>{
 await render();await select('Категорія приймання','other');expect(container.textContent).toContain('Інше');expect(container.querySelectorAll('[data-category-line]')).toHaveLength(50);
 const check=container.querySelector('[aria-label="Вибрати Товар 151"]') as HTMLInputElement;await act(async()=>check.click());await click('Взяти вибрані товари (1)');expect(mock.change.mock.calls[0][2]).toEqual({action:'take',key:'other',lineIds:['line-151']});
});
it('keeps another managers group readonly',async()=>{
 work.groups[0].ownerId='another';await render();expect(button('Взяти категорію в роботу')).toBeUndefined();expect(container.querySelectorAll('input[inputmode=decimal]')).toHaveLength(0);expect(button('Звільнити групу для іншого менеджера')).toBeUndefined();
});
it('retries an uncertain category save with exactly the same request identity and contents',async()=>{
 mock.change.mockRejectedValueOnce(new TypeError('lost response'));await render();await fill('Отримано: Товар 0','3');await click('Зберегти прогрес категорії');expect(button('Перевірити тим самим запитом')).toBeTruthy();await click('Перевірити тим самим запитом');expect(mock.change.mock.calls[1][2]).toEqual(mock.change.mock.calls[0][2]);expect(changed).toHaveBeenCalledOnce();
});

it('completed goods remain readonly until the owner reopens the category on the server',async()=>{
 work.groups[0].ready=true;mock.change.mockImplementation(async(_id:string,_location:string,command:ReceiptWorkCommand)=>{expect(command.action).toBe('reopen');work.groups[0].ready=false;return structuredClone(work);});
 await render();expect(container.querySelectorAll('input[inputmode=decimal]')).toHaveLength(0);await click('Повернути категорію до перевірки');expect(container.querySelectorAll('input[inputmode=decimal]')).toHaveLength(100);expect(button('Завершити категорію').disabled).toBe(true);
});
