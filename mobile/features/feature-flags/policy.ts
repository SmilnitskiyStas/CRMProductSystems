import type { RetailFeatureConfig, RetailFeatureKey } from '@/features/mobile-config/types';

export function retailFeatureEnabled(
  features: RetailFeatureConfig,
  feature: RetailFeatureKey
): boolean {
  return features[feature] === true;
}

export function featureRequirementMet(
  features: RetailFeatureConfig,
  feature?: RetailFeatureKey
): boolean {
  return feature === undefined || retailFeatureEnabled(features, feature);
}
