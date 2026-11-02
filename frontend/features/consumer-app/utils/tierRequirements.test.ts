// @vitest-environment node
import { describe, expect, it } from 'vitest';
import { describeTierRequirements, getTierRatingError, hasTierRequirements } from './tierRequirements';

describe('business-specific tier conditions', () => {
  it('allows combined requirements to replace an unused legacy rating threshold', () => {
    const rules = { minCompositeScore: 100, minPurchaseCount: 5, minCashSpend: 1000, requireCompletedProfile: true };
    expect(hasTierRequirements(rules)).toBe(true);
    expect(getTierRatingError(rules)).toBeNull();
    expect(describeTierRequirements(rules)).toHaveLength(3);
  });

  it.each([100, 500])('rejects an unreachable rating %i when no explicit conditions are selected', (minCompositeScore) => {
    expect(getTierRatingError({ minCompositeScore })).toContain('рейтинг має шкалу до 5');
  });

  it('keeps a valid rating available for businesses choosing that progression model', () => {
    expect(getTierRatingError({ minCompositeScore: 4.5 })).toBeNull();
    expect(hasTierRequirements({ minCompositeScore: 4.5, minCashSpend: null })).toBe(false);
  });

  it('distinguishes a configured zero threshold from a disabled condition', () => {
    expect(hasTierRequirements({ minPurchaseCount: 0 })).toBe(true);
    expect(describeTierRequirements({ minPurchaseCount: 0 })).toHaveLength(1);
    expect(describeTierRequirements({ minPurchaseCount: null })).toEqual([]);
  });
});
