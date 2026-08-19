import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  clearOperationalDraft,
  draftStorageKey,
  legacyDraftStorageKey,
  loadOperationalDraft,
  resetDraftQueuesForTests,
  saveOperationalDraft,
  type OperationalDraft,
} from '../storage';

jest.mock('@react-native-async-storage/async-storage', () =>
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('@react-native-async-storage/async-storage/jest/async-storage-mock'));

const owner = { tenantId: 'tenant-a', userId: 'user-a' };
const writeOff: OperationalDraft = {
  version: 1,
  owner,
  scope: 'create',
  payload: {
    kind: 'write-off',
    locationId: 'loc-1',
    reason: 'damaged',
    notes: 'box torn',
    items: [{
      productId: 'p1',
      productStockId: 's1',
      productName: 'Milk',
      batchNumber: 'B-1',
      expiryDate: '2026-08-01',
      quantity: 2,
      availableQty: 5,
    }],
  },
  submission: { status: 'failed', message: 'saved' },
  updatedAt: '2026-07-29T10:00:00Z',
};

beforeEach(async () => {
  resetDraftQueuesForTests();
  await AsyncStorage.clear();
});

it('persists and restores every operational field', async () => {
  await saveOperationalDraft(writeOff);
  await expect(loadOperationalDraft(owner, 'write-off')).resolves.toEqual(writeOff);
});

it('isolates operations and clears only the selected operation', async () => {
  const production: OperationalDraft = {
    ...writeOff,
    payload: { kind: 'production', locationId: 'loc-1', recipeId: 'r1', plannedQty: '2.5', notes: 'night' },
  };
  await Promise.all([saveOperationalDraft(writeOff), saveOperationalDraft(production)]);
  await clearOperationalDraft(owner, 'write-off');
  await expect(loadOperationalDraft(owner, 'write-off')).resolves.toBeNull();
  await expect(loadOperationalDraft(owner, 'production')).resolves.toEqual(production);
});

it('keeps each owner draft isolated and restores the first owner after switching back', async () => {
  await saveOperationalDraft(writeOff);
  const otherOwner = { tenantId: 'tenant-a', userId: 'user-b' };
  const payload = writeOff.payload.kind === 'write-off' ? writeOff.payload : fail('fixture');
  const otherDraft: OperationalDraft = {
    ...writeOff,
    owner: otherOwner,
    payload: { ...payload, notes: 'other owner' },
  };
  await saveOperationalDraft(otherDraft);

  await expect(loadOperationalDraft(otherOwner, 'write-off')).resolves.toEqual(otherDraft);
  await expect(loadOperationalDraft(owner, 'write-off')).resolves.toEqual(writeOff);
});

it('rejects and removes corrupt and wrong-schema owner snapshots', async () => {
  await AsyncStorage.setItem(draftStorageKey(owner, 'transfer'), '{broken');
  await expect(loadOperationalDraft(owner, 'transfer')).resolves.toBeNull();
  await AsyncStorage.setItem(draftStorageKey(owner, 'receipt'), JSON.stringify({ ...writeOff, payload: { kind: 'receipt', receiptId: 'r', items: [] }, version: 9 }));
  await expect(loadOperationalDraft(owner, 'receipt')).resolves.toBeNull();
});

it('clears only the current owner operation', async () => {
  const otherOwner = { tenantId: 'tenant-a', userId: 'user-b' };
  const otherDraft: OperationalDraft = { ...writeOff, owner: otherOwner };
  await Promise.all([saveOperationalDraft(writeOff), saveOperationalDraft(otherDraft)]);

  await clearOperationalDraft(otherOwner, 'write-off');

  await expect(loadOperationalDraft(otherOwner, 'write-off')).resolves.toBeNull();
  await expect(loadOperationalDraft(owner, 'write-off')).resolves.toEqual(writeOff);
});

it('migrates a matching legacy shared draft without exposing or deleting it for another owner', async () => {
  const foreignOwner = { tenantId: 'tenant-a', userId: 'user-b' };
  await AsyncStorage.setItem(legacyDraftStorageKey('write-off'), JSON.stringify(writeOff));

  await expect(loadOperationalDraft(foreignOwner, 'write-off')).resolves.toBeNull();
  expect(await AsyncStorage.getItem(legacyDraftStorageKey('write-off'))).not.toBeNull();

  await expect(loadOperationalDraft(owner, 'write-off')).resolves.toEqual(writeOff);
  expect(await AsyncStorage.getItem(legacyDraftStorageKey('write-off'))).toBeNull();
  expect(await AsyncStorage.getItem(draftStorageKey(owner, 'write-off'))).not.toBeNull();
});

it('strips secrets and arbitrary fields through the whitelist', async () => {
  const payload = writeOff.payload.kind === 'write-off' ? writeOff.payload : fail('fixture');
  await saveOperationalDraft({
    ...writeOff,
    payload: {
      ...payload,
      token: 'auth-secret',
      qrCode: 'rotating-secret',
      recoveryCode: 'AAAA-BBBB',
      items: [{ ...payload.items[0], totp: '123456' }],
    } as unknown as typeof payload,
  });
  const raw = await AsyncStorage.getItem(draftStorageKey(owner, 'write-off'));
  expect(raw).not.toContain('auth-secret');
  expect(raw).not.toContain('rotating-secret');
  expect(raw).not.toContain('AAAA-BBBB');
  expect(raw).not.toContain('123456');
});

it.each(['failed', 'uncertain', 'conflict'] as const)('retains %s submission state', async (status) => {
  await saveOperationalDraft({ ...writeOff, submission: { status, message: status } });
  expect((await loadOperationalDraft(owner, 'write-off'))?.submission.status).toBe(status);
});

it('serializes rapid writes so the latest value wins', async () => {
  const calls: { value: string; resolve: () => void }[] = [];
  jest.spyOn(AsyncStorage, 'setItem').mockImplementation((_key, value) =>
    new Promise<void>((resolve) => calls.push({ value, resolve })));
  const first = saveOperationalDraft(writeOff);
  const payload = writeOff.payload.kind === 'write-off' ? writeOff.payload : fail('fixture');
  const second = saveOperationalDraft({ ...writeOff, payload: { ...payload, notes: 'latest' } });
  await new Promise<void>((resolve) => setImmediate(resolve));
  expect(calls).toHaveLength(1);
  calls[0].resolve();
  await first;
  await new Promise<void>((resolve) => setImmediate(resolve));
  expect(calls).toHaveLength(2);
  calls[1].resolve();
  await second;
  expect(JSON.parse(calls[1].value).payload.notes).toBe('latest');
});
