import type { SaleFailureKind, SaleGatewayResult, SaleReceipt, SaleSubmitRequest } from './types';

/**
 * Maps an HTTP outcome of POST /api/pos/sales to a failure kind, mirroring
 * mobile/features/pos/saleSubmission.ts plus 423 (expired goods) and 401 (operator session ended).
 * `status` undefined means no response was received (network error / timeout): the server may or
 * may not have stored the sale, so the result is "uncertain".
 */
export function classifySaleFailure(
  status: number | undefined,
  serverMessage?: string,
): { kind: SaleFailureKind; message: string } {
  if (status === undefined) {
    return {
      kind: 'uncertain',
      message:
        'Сервер не підтвердив результат. Натисніть «Повторити» - повторна відправка безпечна, дубль не створиться.',
    };
  }
  if (status === 401) return { kind: 'unauthorized', message: 'Сесію завершено. Увійдіть за PIN-кодом знову.' };
  if (status === 409) {
    return {
      kind: 'conflict',
      message: serverMessage ?? 'Зміна або залишки змінилися. Перевірте зміну перед повтором.',
    };
  }
  if (status === 423) {
    return { kind: 'locked', message: serverMessage ?? 'У кошику є товар з вичерпаним терміном придатності.' };
  }
  if (status >= 500) {
    // Backend may have committed before failing to answer: treat like uncertain, retry is idempotent.
    return {
      kind: 'uncertain',
      message: 'Помилка сервера. Натисніть «Повторити» - повторна відправка безпечна.',
    };
  }
  return { kind: 'failed', message: serverMessage ?? 'Продаж не виконано. Дані кошика збережено.' };
}

export type SubmissionStatus = 'idle' | 'pending' | 'completed' | SaleFailureKind;

export interface SubmissionState {
  status: SubmissionStatus;
  message?: string;
  clientTransactionId?: string;
  sale?: SaleReceipt;
}

export type SaleDraft = Omit<SaleSubmitRequest, 'clientTransactionId'>;

/**
 * Single-flight sale submission state machine. The ClientTransactionId is generated on the first
 * attempt and reused for retries after an uncertain/failed result (the server is idempotent on it),
 * until the sale completes or the caller resets (cart/payment changed -> new sale, new id).
 */
export class SaleSubmitter {
  private current: SubmissionState = { status: 'idle' };
  private inFlight: Promise<SubmissionState> | null = null;

  constructor(
    private readonly gateway: (req: SaleSubmitRequest) => Promise<SaleGatewayResult>,
    private readonly newId: () => string,
    private readonly onChange?: (state: SubmissionState) => void,
  ) {}

  get state(): SubmissionState {
    return this.current;
  }

  private set(next: SubmissionState): void {
    this.current = next;
    this.onChange?.(next);
  }

  submit(draft: SaleDraft): Promise<SubmissionState> {
    if (this.inFlight) return this.inFlight; // double Enter / double click -> one request
    if (this.current.status === 'completed') return Promise.resolve(this.current);

    const clientTransactionId = this.current.clientTransactionId ?? this.newId();
    this.set({ status: 'pending', clientTransactionId });

    this.inFlight = (async () => {
      try {
        const res = await this.gateway({ ...draft, clientTransactionId });
        this.set(
          res.ok
            ? { status: 'completed', clientTransactionId, sale: res.sale }
            : { status: res.kind, clientTransactionId, message: res.message },
        );
      } catch {
        this.set({ status: 'uncertain', clientTransactionId, message: classifySaleFailure(undefined).message });
      } finally {
        this.inFlight = null;
      }
      return this.current;
    })();
    return this.inFlight;
  }

  /** Starts over with a fresh ClientTransactionId (new sale, or after the user changed the cart). */
  reset(): void {
    if (this.inFlight) return;
    this.set({ status: 'idle' });
  }
}
