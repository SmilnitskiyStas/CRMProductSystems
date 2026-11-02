import type { UpsertTierRequest } from '../types';

export const TIER_REQUIREMENT_FIELDS = [
  { key: 'minMembershipDays', label: 'Дні участі у програмі', unit: 'дн.', step: 1 },
  { key: 'minEarnedBonuses', label: 'Нараховані бонуси за покупки', unit: 'бонусів', step: 0.01 },
  { key: 'minCashSpend', label: 'Сума покупок', unit: 'грн', step: 0.01 },
  { key: 'minBonusSpend', label: 'Використані бонуси', unit: 'бонусів', step: 0.01 },
  { key: 'minPurchaseCount', label: 'Покупки з бонусною карткою', unit: 'покупок', step: 1 },
  { key: 'minReviewCount', label: 'Відгуки', unit: 'відгуків', step: 1 },
] as const;

type Rules = Partial<Pick<UpsertTierRequest, 'requireCompletedProfile' | 'minCompositeScore' | typeof TIER_REQUIREMENT_FIELDS[number]['key']>>;

export function hasTierRequirements(tier: Rules): boolean {
  return tier.requireCompletedProfile === true || TIER_REQUIREMENT_FIELDS.some(({ key }) => tier[key] != null);
}

export function getTierRatingError(tier: Rules): string | null {
  return !hasTierRequirements(tier) && (tier.minCompositeScore ?? 0) > 5
    ? 'Цей поріг недосяжний: рейтинг має шкалу до 5. Додайте конкретні умови переходу або задайте рейтинг від 0 до 5.'
    : null;
}

export function describeTierRequirements(tier: Rules): string[] {
  const conditions: string[] = [];
  if (tier.requireCompletedProfile) conditions.push('Заповнити ім’я та email у профілі');
  for (const { key, label, unit } of TIER_REQUIREMENT_FIELDS) {
    const value = tier[key];
    if (value != null) conditions.push(`${label}: не менше ${new Intl.NumberFormat('uk-UA', { maximumFractionDigits: 2 }).format(value)} ${unit}`);
  }
  return conditions;
}
