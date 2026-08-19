import { createMockMobileConfig } from '@/features/mobile-config/mock';
import { createRetailThemeTokens } from '../tokens';

describe('retail theme tokens', () => {
  test('maps whitelisted theme configuration to semantic tokens', () => {
    const config = createMockMobileConfig('tenant-a');
    const tokens = createRetailThemeTokens(config.theme);

    expect(tokens.colors.primary).toBe('#16a34a');
    expect(tokens.colors.onPrimary).toBe('#FFFFFF');
    expect(tokens.colors.border).toMatch(/^#[0-9a-f]{6}$/);
    expect(tokens.radius).toEqual({ button: 14, card: 18 });
    expect(tokens.spacing).toEqual({ xs: 4, sm: 8, md: 16, lg: 24, xl: 32 });
  });

  test('chooses readable foreground text for light tenant colors', () => {
    const config = createMockMobileConfig('tenant-a');
    config.theme.colors.primary = '#FFD600';
    expect(createRetailThemeTokens(config.theme).colors.onPrimary).toBe('#000000');
  });
});
