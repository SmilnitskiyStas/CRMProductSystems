export interface RetailTenant {
  id: string;
  slug: string;
  name: string;
  logoUrl: string | null;
}

export type ActiveTenantHydrationStatus = 'idle' | 'pending' | 'ready' | 'error';
