import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  loadPosDraft,
  POS_DRAFT_STORAGE_KEY,
  POS_DRAFT_VERSION,
  savePosDraft,
  sanitizePosDraft,
  type PosDraftSnapshot,
} from '../draftStorage';

jest.mock('@react-native-async-storage/async-storage', () =>
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('@react-native-async-storage/async-storage/jest/async-storage-mock')
);

const owner = { tenantId: 'tenant-a', userId: 'user-a' };
const draft: PosDraftSnapshot = {
  version: POS_DRAFT_VERSION,
  owner,
  shiftId: 'shift-a',
  cart: [
    {
      barcode: '123',
      quantity: 2,
      productName: 'Milk',
      unitPrice: 42,
    },
  ],
  customer: {
    customerId: 'customer-a',
    customerName: 'Customer',
    membershipId: 'membership-a',
    redeemAmount: 5,
  },
  paymentType: 'Cash',
  cashReceived: '100',
  printReceipt: true,
  submission: { status: 'idle' },
  updatedAt: '2026-07-29T00:00:00.000Z',
};

describe('POS draft storage', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
  });

  it('persists and restores a valid operational draft', async () => {
    await savePosDraft(draft);
    await expect(loadPosDraft(owner)).resolves.toEqual(draft);
  });

  it.each([
    ['wrong tenant', { tenantId: 'tenant-b', userId: 'user-a' }],
    ['wrong user', { tenantId: 'tenant-a', userId: 'user-b' }],
  ])('rejects and removes a %s snapshot', async (_label, otherOwner) => {
    await savePosDraft(draft);
    await expect(loadPosDraft(otherOwner)).resolves.toBeNull();
    await expect(AsyncStorage.getItem(POS_DRAFT_STORAGE_KEY)).resolves.toBeNull();
  });

  it('rejects corrupt and unsupported-version snapshots', async () => {
    await AsyncStorage.setItem(POS_DRAFT_STORAGE_KEY, '{bad-json');
    await expect(loadPosDraft(owner)).resolves.toBeNull();

    await AsyncStorage.setItem(
      POS_DRAFT_STORAGE_KEY,
      JSON.stringify({ ...draft, version: 999 })
    );
    await expect(loadPosDraft(owner)).resolves.toBeNull();
  });

  it('whitelists fields so rotating loyalty and auth secrets are never durable', async () => {
    const contaminated = {
      ...draft,
      loyaltyQrCode: 'SGLOY1.secret.123456',
      totpSecret: 'totp-secret',
      recoveryCode: 'ABCD-EFGH',
      accessToken: 'jwt',
      customer: {
        ...draft.customer,
        rawCode: 'SGLOY1.secret.123456',
        challengeToken: 'challenge',
      },
    } as PosDraftSnapshot;

    const json = JSON.stringify(sanitizePosDraft(contaminated));
    expect(json).not.toContain('SGLOY1');
    expect(json).not.toContain('totp-secret');
    expect(json).not.toContain('ABCD-EFGH');
    expect(json).not.toContain('jwt');
    expect(json).not.toContain('challenge');
  });
});
