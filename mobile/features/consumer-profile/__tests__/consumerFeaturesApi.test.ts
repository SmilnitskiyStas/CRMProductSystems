import MockAdapter from 'axios-mock-adapter';
import { personalApiClient } from '@/lib/api-client';
import { changeConsumerPhone, getConsumerProfile, updateConsumerProfile } from '../api';
import { createTicket, getMyTickets } from '@/features/consumer-support/api';
import { createPurchaseReview } from '@/features/purchase-reviews/api';
import { getLoyaltyTierLadder, getLoyaltyTierProgress } from '@/features/loyalty/api/loyaltyApi';

const mock = new MockAdapter(personalApiClient);
afterEach(()=>mock.reset());

test('uses consumer profile self-service routes',async()=>{
 const profile={consumerAccountId:'c1',fullName:'Олена',email:'o@example.com',phone:'+3801',registeredAt:'2026-01-01'};
 mock.onGet('/consumer/profile').reply(200,profile);
 mock.onPut('/consumer/profile').reply(config=>{expect(JSON.parse(config.data)).toEqual({fullName:'Нове ім’я',email:''});return[200,{...profile,fullName:'Нове ім’я',email:null}];});
 mock.onPut('/consumer/profile/phone').reply(config=>{expect(JSON.parse(config.data)).toEqual({newPhone:'+3802',currentPassword:'secret'});return[200,{...profile,phone:'+3802'}];});
 await expect(getConsumerProfile()).resolves.toEqual(profile);
 await updateConsumerProfile({fullName:'Нове ім’я',email:''});
 await changeConsumerPhone({newPhone:'+3802',currentPassword:'secret'});
});

test('passes the selected tenant to support and tier routes',async()=>{
 mock.onGet('/consumer/support/tickets').reply(config=>{expect(config.params.tenantId).toBe('tenant-1');return[200,{items:[],totalCount:0,page:1,pageSize:50,totalPages:0}];});
 mock.onPost('/consumer/support/tickets').reply(config=>{expect(JSON.parse(config.data).tenantId).toBe('tenant-1');return[201,{id:'t1'}];});
 mock.onGet('/consumer/loyalty/tenant-1/tiers').reply(200,{currentTierName:'Gold'});
 mock.onGet('/consumer/loyalty/tenant-1/tiers/ladder').reply(200,[{id:'gold',name:'Gold',sortOrder:1,minCompositeScore:100,accrualMultiplier:1.5,discountPercent:5}]);
 await getMyTickets('tenant-1'); await createTicket({tenantId:'tenant-1',subject:'Питання',body:'Текст'});
 await expect(getLoyaltyTierProgress('tenant-1')).resolves.toEqual({currentTierName:'Gold'});
 await expect(getLoyaltyTierLadder('tenant-1')).resolves.toEqual([expect.objectContaining({name:'Gold'})]);
});

test('creates a review for a concrete POS transaction',async()=>{
 mock.onPost('/consumer/reviews').reply(config=>{expect(JSON.parse(config.data)).toEqual({tenantId:'tenant-1',posTransactionId:'tx-1',rating:5,comment:'Чудово'});return[201,{id:'r1'}];});
 await createPurchaseReview({tenantId:'tenant-1',posTransactionId:'tx-1',rating:5,comment:'Чудово'});
});
