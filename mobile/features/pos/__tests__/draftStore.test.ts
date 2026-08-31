import AsyncStorage from '@react-native-async-storage/async-storage';
import {
  flushPosDraftPersistenceForTests,
  usePosDraftStore,
} from '../draftStore';
import {
  POS_DRAFT_STORAGE_KEY,
  POS_DRAFT_VERSION,
  savePosDraft,
  type PosDraftSnapshot,
} from '../draftStorage';

jest.mock('@react-native-async-storage/async-storage', () =>
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  require('@react-native-async-storage/async-storage/jest/async-storage-mock')
);

const owner = { tenantId: 'tenant', userId: 'user' };
const restored: PosDraftSnapshot = {
  version: POS_DRAFT_VERSION,
  owner,
  shiftId: 'shift',
  cart: [{ barcode: '1', quantity: 3, productName: 'Item', unitPrice: 2 }],
  customer: { customerId: 'customer', membershipId: 'membership' },
  paymentType: 'Card',
  cashReceived: '',
  printReceipt: true,
  submission: { status: 'pending' },
  updatedAt: new Date().toISOString(),
};

describe('POS draft store', () => {
  beforeEach(async () => {
    await flushPosDraftPersistenceForTests();
    await AsyncStorage.clear();
    usePosDraftStore.setState({
      hydrated: false,
      owner: null,
      shiftId: '',
      cart: [],
      customer: null,
      paymentType: 'Cash',
      cashReceived: '',
      submission: { status: 'idle' },
    });
  });

  it('restores cart, shift, quantities and customer; interrupted pending becomes uncertain', async () => {
    await savePosDraft(restored);
    await usePosDraftStore.getState().bindOwner(owner);
    expect(usePosDraftStore.getState()).toMatchObject({
      shiftId: 'shift',
      cart: restored.cart,
      customer: restored.customer,
      submission: { status: 'uncertain' },
    });
  });

  it('does not clear a failed draft', async () => {
    await savePosDraft(restored);
    await usePosDraftStore.getState().bindOwner(owner);
    await usePosDraftStore.getState().setSubmission('failed', 'failed');
    await usePosDraftStore.getState().clearAfterConfirmedSale();
    expect(await AsyncStorage.getItem(POS_DRAFT_STORAGE_KEY)).not.toBeNull();
  });

  it('clears durable and memory draft only after confirmed success', async () => {
    await savePosDraft(restored);
    await usePosDraftStore.getState().bindOwner(owner);
    await usePosDraftStore.getState().setSubmission('completed', undefined, 'tx');
    await usePosDraftStore.getState().clearAfterConfirmedSale();
    expect(await AsyncStorage.getItem(POS_DRAFT_STORAGE_KEY)).toBeNull();
    expect(usePosDraftStore.getState()).toMatchObject({
      shiftId: '',
      cart: [],
      customer: null,
      submission: { status: 'idle' },
    });
  });

  it('does not silently move a restored cart to another shift', async () => {
    await savePosDraft({ ...restored, submission: { status: 'idle' } });
    await usePosDraftStore.getState().bindOwner(owner);
    usePosDraftStore.getState().setShift('different-shift');
    await flushPosDraftPersistenceForTests();

    expect(usePosDraftStore.getState()).toMatchObject({
      shiftId: 'shift',
      cart: restored.cart,
      submission: { status: 'conflict' },
    });
  });

  it('does not clear an uncertain result by editing the cart or customer', async () => {
    await savePosDraft({ ...restored, submission: { status: 'uncertain' } });
    await usePosDraftStore.getState().bindOwner(owner);
    usePosDraftStore.getState().setCart([
      { barcode: '2', quantity: 1, productName: 'Changed', unitPrice: 3 },
    ]);
    usePosDraftStore.getState().setCustomer(null);
    expect(usePosDraftStore.getState().submission.status).toBe('uncertain');
  });

  it('serializes rapid persistence so the latest cart is durable', async () => {
    await usePosDraftStore.getState().bindOwner(owner);
    usePosDraftStore.getState().setShift('shift');
    usePosDraftStore
      .getState()
      .setCart([{ barcode: '1', quantity: 1, productName: 'Item', unitPrice: 2 }]);
    usePosDraftStore
      .getState()
      .setCart([{ barcode: '1', quantity: 9, productName: 'Item', unitPrice: 2 }]);
    await flushPosDraftPersistenceForTests();

    const raw = await AsyncStorage.getItem(POS_DRAFT_STORAGE_KEY);
    expect(JSON.parse(raw as string).cart[0].quantity).toBe(9);
  });
});
