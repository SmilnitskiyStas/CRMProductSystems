import type { PropsWithChildren } from 'react';
import { FeatureFlagProvider } from '@/features/feature-flags/FeatureFlagProvider';
import { LoyaltyTenantBridge } from '@/features/tenant/LoyaltyTenantBridge';
import { TenantMembershipCoordinator } from '@/features/tenant/TenantMembershipCoordinator';
import { ActiveTenantProvider } from '@/features/tenant/ActiveTenantProvider';
import { RetailThemeProvider } from '@/features/theme/RetailThemeProvider';
import { MobileConfigProvider } from './MobileConfigProvider';

export function RetailShellProviders({ children }: PropsWithChildren) {
  return (
    <ActiveTenantProvider>
      <MobileConfigProvider>
        <RetailThemeProvider>
          <FeatureFlagProvider>
            <LoyaltyTenantBridge />
            <TenantMembershipCoordinator />
            {children}
          </FeatureFlagProvider>
        </RetailThemeProvider>
      </MobileConfigProvider>
    </ActiveTenantProvider>
  );
}
