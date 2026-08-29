import AsyncStorage from '@react-native-async-storage/async-storage';
import { createTransfer } from '@/features/transfers/api/transferApi';
import { processItem, confirmReceipt } from '@/features/receipt/api/receiptApi';
import { enqueueOperationalMutation, listOperationalMutations, resetOperationalQueueForTests, syncOperationalMutations } from '../operationalQueue';

jest.mock('@/features/transfers/api/transferApi', () => ({ createTransfer: jest.fn() }));
jest.mock('@/features/production/api', () => ({ createProductionOrder: jest.fn() }));
jest.mock('@/features/receipt/api/receiptApi', () => ({ processItem: jest.fn(), confirmReceipt: jest.fn() }));
jest.mock('@/features/marketplace-orders/api/marketplaceOrdersApi', () => ({
  updateMarketplaceReceiptItem: jest.fn(), finalizeMarketplaceReceipt: jest.fn(),
}));

const owner = { tenantId: 'tenant-1', userId: 'user-1' };

beforeEach(async () => {
  resetOperationalQueueForTests();
  jest.clearAllMocks();
  await AsyncStorage.clear();
});

test('syncs a transfer once and removes the confirmed local operation', async () => {
  const item = await enqueueOperationalMutation(owner, { kind: 'transfer.create', payload: {
    fromLocationId: 'a', toLocationId: 'b', items: [{ productStockId: 'stock-1', quantity: 2 }],
  } });
  jest.mocked(createTransfer).mockResolvedValue({} as never);

  await syncOperationalMutations(owner);
  await syncOperationalMutations(owner);

  expect(createTransfer).toHaveBeenCalledTimes(1);
  expect(createTransfer).toHaveBeenCalledWith(item.mutation.payload, item.operationId);
  expect(await listOperationalMutations(owner)).toEqual([]);
});

test('preserves receipt item-before-confirm ordering', async () => {
  await enqueueOperationalMutation(owner, { kind: 'receipt.item', payload: {
    receiptId: 'receipt-1', itemId: 'item-1', quantityReceived: 4,
  } });
  await enqueueOperationalMutation(owner, { kind: 'receipt.confirm', payload: { receiptId: 'receipt-1' } });
  jest.mocked(processItem).mockResolvedValue({} as never);
  jest.mocked(confirmReceipt).mockResolvedValue({} as never);

  await syncOperationalMutations(owner);

  expect(jest.mocked(processItem).mock.invocationCallOrder[0])
    .toBeLessThan(jest.mocked(confirmReceipt).mock.invocationCallOrder[0]);
});

test('never automatically retries an operation with an uncertain result', async () => {
  await enqueueOperationalMutation(owner, { kind: 'transfer.create', payload: {
    fromLocationId: 'a', toLocationId: 'b', items: [{ productStockId: 'stock-1', quantity: 1 }],
  } });
  jest.mocked(createTransfer).mockRejectedValue(new Error('network'));

  await syncOperationalMutations(owner);
  await syncOperationalMutations(owner);

  expect(createTransfer).toHaveBeenCalledTimes(1);
  expect(await listOperationalMutations(owner)).toEqual([expect.objectContaining({ status: 'uncertain' })]);
});
