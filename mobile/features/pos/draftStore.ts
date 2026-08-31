import { create } from 'zustand';
import {
  clearPosDraft,
  loadPosDraft,
  POS_DRAFT_VERSION,
  savePosDraft,
  type PosDraftCustomer,
  type PosDraftOwner,
  type PosDraftSnapshot,
  type SaleSubmissionStatus,
} from './draftStorage';
import type { PaymentType } from './types';

type DraftCart = PosDraftSnapshot['cart'];

interface PosDraftState {
  hydrated: boolean;
  owner: PosDraftOwner | null;
  shiftId: string;
  cart: DraftCart;
  customer: PosDraftCustomer | null;
  paymentType: PaymentType;
  cashReceived: string;
  printReceipt: boolean;
  submission: PosDraftSnapshot['submission'];
  bindOwner: (owner: PosDraftOwner) => Promise<void>;
  setShift: (shiftId: string) => void;
  setCart: (cart: DraftCart) => void;
  setCustomer: (customer: PosDraftCustomer | null) => void;
  setPayment: (paymentType: PaymentType, cashReceived: string) => void;
  setPrintReceipt: (printReceipt: boolean) => void;
  setSubmission: (
    status: SaleSubmissionStatus,
    message?: string,
    transactionId?: string
  ) => Promise<void>;
  clearAfterConfirmedSale: () => Promise<void>;
  discard: () => Promise<void>;
}

const empty = {
  shiftId: '',
  cart: [] as DraftCart,
  customer: null,
  paymentType: 'Cash' as PaymentType,
  cashReceived: '',
  printReceipt: true,
  submission: { status: 'idle' as SaleSubmissionStatus },
};

function snapshot(state: PosDraftState): PosDraftSnapshot | null {
  if (!state.owner || !state.shiftId) return null;
  return {
    version: POS_DRAFT_VERSION,
    owner: state.owner,
    shiftId: state.shiftId,
    cart: state.cart,
    customer: state.customer,
    paymentType: state.paymentType,
    cashReceived: state.cashReceived,
    printReceipt: state.printReceipt,
    submission: state.submission,
    updatedAt: new Date().toISOString(),
  };
}

let persistenceQueue: Promise<void> = Promise.resolve();

function persist(): void {
  // Serialize writes and read the latest state only when this queued turn runs.
  // Rapid scans can enqueue many turns, but an older snapshot can never finish last.
  persistenceQueue = persistenceQueue
    .catch(() => undefined)
    .then(async () => {
      const value = snapshot(usePosDraftStore.getState());
      if (value) await savePosDraft(value);
    });
}

export function flushPosDraftPersistenceForTests(): Promise<void> {
  return persistenceQueue;
}

export const usePosDraftStore = create<PosDraftState>((set, get) => ({
  hydrated: false,
  owner: null,
  ...empty,
  bindOwner: async (owner) => {
    const currentOwner = get().owner;
    if (
      currentOwner?.tenantId !== owner.tenantId ||
      currentOwner?.userId !== owner.userId
    ) {
      // Fail closed immediately so a newly logged-in cashier never renders the
      // previous cashier's in-memory cart while AsyncStorage is being checked.
      set({ hydrated: false, owner, ...empty });
    }
    const restored = await loadPosDraft(owner);
    if (restored) {
      set({
        hydrated: true,
        owner,
        shiftId: restored.shiftId,
        cart: restored.cart,
        customer: restored.customer,
        paymentType: restored.paymentType,
        cashReceived: restored.cashReceived,
        printReceipt: restored.printReceipt !== false,
        submission:
          restored.submission.status === 'pending'
            ? {
                status: 'uncertain',
                message:
                  'Застосунок закрився під час відправлення. Звірте продажі зміни перед повтором.',
              }
            : restored.submission,
      });
      persist();
      return;
    }
    set({ hydrated: true, owner, ...empty });
  },
  setShift: (shiftId) => {
    const current = get();
    if (current.shiftId && current.shiftId !== shiftId && current.cart.length > 0) {
      set({
        submission: {
          status: 'conflict',
          message:
            'Збережений кошик належить іншій зміні. Звірте стару зміну або явно відкиньте чернетку.',
        },
      });
      persist();
      return;
    }
    set({ shiftId });
    persist();
  },
  setCart: (cart) => {
    const status = get().submission.status;
    set({
      cart,
      ...(status === 'uncertain' || status === 'conflict'
        ? {}
        : { submission: { status: 'idle' as SaleSubmissionStatus } }),
    });
    persist();
  },
  setCustomer: (customer) => {
    const status = get().submission.status;
    set({
      customer,
      ...(status === 'uncertain' || status === 'conflict'
        ? {}
        : { submission: { status: 'idle' as SaleSubmissionStatus } }),
    });
    persist();
  },
  setPayment: (paymentType, cashReceived) => {
    set({ paymentType, cashReceived });
    persist();
  },
  setPrintReceipt: (printReceipt) => {
    set({ printReceipt });
    persist();
  },
  setSubmission: async (status, message, transactionId) => {
    set({
      submission: {
        status,
        ...(message ? { message } : {}),
        ...(transactionId ? { transactionId } : {}),
      },
    });
    const value = snapshot(get());
    if (value) await savePosDraft(value);
  },
  clearAfterConfirmedSale: async () => {
    if (get().submission.status !== 'completed') return;
    await clearPosDraft();
    set({ ...empty });
  },
  discard: async () => {
    await clearPosDraft();
    set({ ...empty });
  },
}));
