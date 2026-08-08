import type { AuthUser } from '@/features/auth/types';

export interface ModulesSettings {
  businessType: string;
  modules: string[];
}

export interface NavigationContext {
  user: AuthUser | null;
  settings: ModulesSettings | null;
}

export type NavigationDecision =
  | { allowed: true }
  | { allowed: false; reason: 'access_denied' | 'module_disabled' | 'context_unavailable' };
