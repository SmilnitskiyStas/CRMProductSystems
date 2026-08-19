import { createContext, useContext, useMemo, type PropsWithChildren } from 'react';
import { useMobileConfig } from '@/features/mobile-config/MobileConfigProvider';
import { createRetailThemeTokens, type RetailThemeTokens } from './tokens';

const RetailThemeContext = createContext<RetailThemeTokens | null>(null);

export function RetailThemeProvider({ children }: PropsWithChildren) {
  const { config } = useMobileConfig();
  const tokens = useMemo(() => createRetailThemeTokens(config.theme), [config.theme]);
  return <RetailThemeContext.Provider value={tokens}>{children}</RetailThemeContext.Provider>;
}

export function useRetailTheme(): RetailThemeTokens {
  const value = useContext(RetailThemeContext);
  if (!value) throw new Error('useRetailTheme must be used within RetailThemeProvider');
  return value;
}
