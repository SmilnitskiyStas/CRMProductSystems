import type { RetailThemeConfig } from '@/features/mobile-config/types';

export interface RetailThemeTokens {
  colors: RetailThemeConfig['colors'] & {
    onPrimary: '#000000' | '#FFFFFF';
    border: string;
  };
  radius: { button: number; card: number };
  spacing: { xs: number; sm: number; md: number; lg: number; xl: number };
}

const SPACING = {
  compact: { xs: 4, sm: 8, md: 12, lg: 16, xl: 24 },
  comfortable: { xs: 4, sm: 8, md: 16, lg: 24, xl: 32 },
} as const;

function readableTextOn(hex: string): '#000000' | '#FFFFFF' {
  const red = Number.parseInt(hex.slice(1, 3), 16);
  const green = Number.parseInt(hex.slice(3, 5), 16);
  const blue = Number.parseInt(hex.slice(5, 7), 16);
  const luminance = (0.299 * red + 0.587 * green + 0.114 * blue) / 255;
  return luminance > 0.6 ? '#000000' : '#FFFFFF';
}

function mixWithBackground(hex: string): string {
  const channels = [1, 3, 5].map((index) => Number.parseInt(hex.slice(index, index + 2), 16));
  return `#${channels
    .map((channel) => Math.round(channel * 0.15 + 255 * 0.85).toString(16).padStart(2, '0'))
    .join('')}`;
}

export function createRetailThemeTokens(config: RetailThemeConfig): RetailThemeTokens {
  return {
    colors: {
      ...config.colors,
      onPrimary: readableTextOn(config.colors.primary),
      border: mixWithBackground(config.colors.textSecondary),
    },
    radius: { button: config.buttons.radius, card: config.cards.radius },
    spacing: SPACING[config.spacing],
  };
}
