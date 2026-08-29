import AsyncStorage from '@react-native-async-storage/async-storage';
import { createWriteOff } from '@/features/write-offs/api/writeOffApi';
import {
  enqueueWriteOff,
  listQueuedWriteOffs,
  resetWriteOffQueueLocksForTests,
  syncQueuedWriteOffs,
} from '../writeOffQueue';

jest.mock('@/features/write-offs/api/writeOffApi', () => ({ createWriteOff: jest.fn() }));

const owner = { tenantId: 'tenant-1', userId: 'user-1' };
const payload = {
  locationId: 'store-1',
  reason: 'damaged' as const,
  items: [{ productId: 'product-1', quantity: 2, unitPrice: 20, isReturnedToSupplier: false }],
};
const mockedCreate = jest.mocked(createWriteOff);

beforeEach(async () => {
  resetWriteOffQueueLocksForTests();
  mockedCreate.mockReset();
  await AsyncStorage.clear();
});

test('stores locally first and removes only a server-confirmed operation', async () => {
  const queued = await enqueueWriteOff(owner, payload);
  expect(await listQueuedWriteOffs(owner)).toEqual([expect.objectContaining({ status: 'queued' })]);
  mockedCreate.mockResolvedValue({} as never);

  await expect(syncQueuedWriteOffs(owner)).resolves.toEqual({ synced: 1, failed: 0, uncertain: 0 });
  expect(mockedCreate).toHaveBeenCalledTimes(1);
  expect(mockedCreate).toHaveBeenCalledWith(payload, queued.operationId);
  expect(await listQueuedWriteOffs(owner)).toEqual([]);

  await syncQueuedWriteOffs(owner);
  expect(mockedCreate).toHaveBeenCalledTimes(1);
});

test('does not automatically retry an unconfirmed request', async () => {
  await enqueueWriteOff(owner, payload);
  mockedCreate.mockRejectedValue(Object.assign(new Error('timeout'), { code: 'ETIMEDOUT' }));

  await expect(syncQueuedWriteOffs(owner)).resolves.toEqual({ synced: 0, failed: 0, uncertain: 1 });
  await syncQueuedWriteOffs(owner);

  expect(mockedCreate).toHaveBeenCalledTimes(1);
  expect(await listQueuedWriteOffs(owner)).toEqual([
    expect.objectContaining({ status: 'uncertain', attempts: 1 }),
  ]);
});

test('isolates queues belonging to different signed-in users', async () => {
  const other = { tenantId: 'tenant-1', userId: 'user-2' };
  await enqueueWriteOff(owner, payload);
  await enqueueWriteOff(other, payload);
  mockedCreate.mockResolvedValue({} as never);

  await syncQueuedWriteOffs(owner);
  expect(await listQueuedWriteOffs(owner)).toEqual([]);
  expect(await listQueuedWriteOffs(other)).toHaveLength(1);
});
