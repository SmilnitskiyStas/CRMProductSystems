import { createContext, useContext, useMemo, type PropsWithChildren } from 'react';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import type { RetailFeatureConfig, RetailFeatureKey } from '@/features/mobile-config/types';
import { retailFeatureEnabled } from './policy';

interface FeatureFlagContextValue {
  features: RetailFeatureConfig;
  isEnabled: (feature: RetailFeatureKey) => boolean;
}

const FeatureFlagContext = createContext<FeatureFlagContextValue | null>(null);

export function FeatureFlagProvider({ children }: PropsWithChildren) {
  const { config } = useMobileConfig();
  const value = useMemo<FeatureFlagContextValue>(
    () => ({
      features: config.features,
      isEnabled: (feature) => retailFeatureEnabled(config.features, feature),
    }),
    [config.features]
  );
  return <FeatureFlagContext.Provider value={value}>{children}</FeatureFlagContext.Provider>;
}

export function useRetailFeature(feature: RetailFeatureKey): boolean {
  const value = useContext(FeatureFlagContext);
  if (!value) throw new Error('useRetailFeature must be used within FeatureFlagProvider');
  return value.isEnabled(feature);
}
