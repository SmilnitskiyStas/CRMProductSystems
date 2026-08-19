export const colors = {
  brand: { 50: '#f0fdf4', 100: '#dcfce7', 500: '#22c55e', 600: '#16a34a', 700: '#15803d' },
  neutral: { 0: '#ffffff', 50: '#f9fafb', 100: '#f3f4f6', 200: '#e5e7eb', 400: '#9ca3af', 500: '#6b7280', 700: '#374151', 900: '#111827' },
  status: { success: '#15803d', warning: '#b45309', danger: '#b91c1c', info: '#1d4ed8' },
} as const;

export const typography = {
  size: { caption: 12, body: 16, title: 24, display: 30 },
  weight: { regular: '400', medium: '500', semibold: '600', bold: '700' },
} as const;

export const spacing = { 1: 4, 2: 8, 3: 12, 4: 16, 5: 20, 6: 24, 8: 32 } as const;
export const radii = { sm: 8, md: 12, lg: 16, full: 999 } as const;
export const touchTarget = 44;
