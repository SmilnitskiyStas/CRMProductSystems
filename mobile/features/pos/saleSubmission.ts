import type { SaleRequest, SaleResponse } from './types';
import type { SaleSubmissionStatus } from './draftStorage';

export interface SubmissionTransition {
  status: SaleSubmissionStatus;
  message?: string;
  transactionId?: string;
}

type Submit = (request: SaleRequest) => Promise<SaleResponse>;
type Transition = (state: SubmissionTransition) => Promise<void>;

let activeSubmission: Promise<SaleResponse | null> | null = null;

function classify(error: unknown): SubmissionTransition {
  const axiosError = error as {
    code?: string;
    response?: { status?: number; data?: { error?: string } };
  };
  const status = axiosError.response?.status;
  const message = axiosError.response?.data?.error;

  if (status === 409) {
    return {
      status: 'conflict',
      message: message ?? 'Зміна або залишки змінилися. Звірте продаж перед повтором.',
    };
  }
  if (!axiosError.response || axiosError.code === 'ECONNABORTED' || axiosError.code === 'ETIMEDOUT') {
    return {
      status: 'uncertain',
      message:
        'Сервер не підтвердив результат. Не повторюйте продаж, доки не звірите операції поточної зміни.',
    };
  }
  return { status: 'failed', message: message ?? 'Продаж не виконано. Дані кошика збережено.' };
}

export function submitSaleSingleFlight(
  request: SaleRequest,
  submit: Submit,
  transition: Transition
): Promise<SaleResponse | null> {
  if (activeSubmission) return activeSubmission;

  activeSubmission = (async () => {
    await transition({ status: 'pending' });
    try {
      const result = await submit(request);
      await transition({
        status: 'completed',
        transactionId: result.transactionId,
      });
      return result;
    } catch (error) {
      await transition(classify(error));
      return null;
    } finally {
      activeSubmission = null;
    }
  })();

  return activeSubmission;
}

export function resetSubmissionLockForTests(): void {
  activeSubmission = null;
}
