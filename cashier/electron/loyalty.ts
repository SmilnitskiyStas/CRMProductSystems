import type { ApiError, LoyaltyMember, Result } from '@shared/types';
import type { HttpResponse } from './http';

/** Wire shape of ResolveLoyaltyCodeResult (tier fields added by the backend, nullable). */
export interface LoyaltyResolveWire {
  membershipId: string;
  customerId: string | null;
  customerName: string | null;
  maskedPhone: string | null;
  balance: number;
  tierId?: string | null;
  tierName?: string | null;
  tierDiscountPercent?: number | null;
}

export type LoyaltyLookup = 'code' | 'identifier';

export const LOYALTY_MESSAGES = {
  offline: 'Немає зв’язку — клієнта можна ідентифікувати лише онлайн.',
  codeInvalid: 'Код недійсний або вже використаний.',
  notFound: 'Клієнта не знайдено.',
  badIdentifier: 'Номер некоректний або членство заблоковано. Перевірте дані.',
  tooMany: 'Забагато спроб, зачекайте.',
  forbidden: 'Немає доступу до програми лояльності.',
  session: 'Сесію завершено. Увійдіть за PIN-кодом знову.',
  server: 'Помилка сервера. Спробуйте ще раз.',
} as const;

/** Readable Ukrainian message for a failed resolve. Server texts are never shown raw and codes are never logged. */
export function mapLoyaltyError(res: Pick<HttpResponse<unknown>, 'status' | 'networkError'>, kind: LoyaltyLookup): ApiError {
  if (res.networkError) return { kind: 'network', message: LOYALTY_MESSAGES.offline };
  switch (res.status) {
    case 401:
      return { kind: 'unauthorized', status: 401, message: LOYALTY_MESSAGES.session };
    case 403:
      return { kind: 'validation', status: 403, message: LOYALTY_MESSAGES.forbidden };
    case 409:
      return { kind: 'conflict', status: 409, message: LOYALTY_MESSAGES.codeInvalid };
    case 429:
      return { kind: 'validation', status: 429, message: LOYALTY_MESSAGES.tooMany };
    case 404:
      return { kind: 'validation', status: 404, message: kind === 'code' ? LOYALTY_MESSAGES.codeInvalid : LOYALTY_MESSAGES.notFound };
    case 400:
      return { kind: 'validation', status: 400, message: kind === 'code' ? LOYALTY_MESSAGES.codeInvalid : LOYALTY_MESSAGES.badIdentifier };
    default:
      return { kind: 'server', status: res.status, message: LOYALTY_MESSAGES.server };
  }
}

export function mapLoyaltyMember(w: LoyaltyResolveWire): LoyaltyMember {
  return {
    membershipId: w.membershipId,
    customerId: w.customerId ?? null,
    customerName: w.customerName ?? null,
    maskedPhone: w.maskedPhone ?? null,
    balance: w.balance,
    tierId: w.tierId ?? null,
    tierName: w.tierName ?? null,
    tierDiscountPercent: w.tierDiscountPercent ?? null,
  };
}

export function toLoyaltyResult(res: HttpResponse<LoyaltyResolveWire>, kind: LoyaltyLookup): Result<LoyaltyMember> {
  if (!res.networkError && res.status === 200 && res.body?.membershipId) {
    return { ok: true, data: mapLoyaltyMember(res.body) };
  }
  return { ok: false, error: mapLoyaltyError(res, kind) };
}
