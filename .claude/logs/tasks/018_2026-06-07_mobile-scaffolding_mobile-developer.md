# TASK-018 — Mobile App Scaffolding
**Agent:** mobile-developer
**Date:** 2026-06-07
**Status:** done

## What was done

Scaffolded the full Expo SDK 56 mobile app from scratch at `/mobile`.

### Config files
- `package.json` — Expo 51, expo-router v3, NativeWind v4, React Query v5, Zustand, react-hook-form + zod, expo-camera, expo-secure-store
- `app.json` — Expo config with scheme `shelfguard`, camera/notifications plugins, typedRoutes
- `tsconfig.json` — strict mode, `@/*` path alias
- `babel.config.js` — NativeWind v4 babel preset
- `metro.config.js` — withNativeWind wrapper
- `tailwind.config.js` — NativeWind preset, custom status colors
- `global.css` — Tailwind directives
- `.env.example` — `EXPO_PUBLIC_API_URL`

### Lib
- `lib/api-client.ts` — axios instance with SecureStore auth header + 401 → refresh retry
- `lib/query-client.ts` — React Query client (2min stale, 1 retry)

### Features
- `features/auth/` — types, authApi, Zustand store (SecureStore), useLogin hook
- `features/dashboard/` — types, dashboardApi, useDashboardStats, StatusCard component
- `features/stock/` — types, stockApi, useStock/useStockBatch/useCreateStockBatch/useVerifyBatch, StockBatchCard component
- `features/receipt/` — types, receiptApi, useReceipts/useReceipt/useConfirmReceipt, ReceiptItemRow component

### App screens (Expo Router file-based)
- `app/_layout.tsx` — root: QueryClientProvider + SafeAreaProvider, loads SecureStore token on mount
- `app/(auth)/_layout.tsx` — Stack navigator
- `app/(auth)/login.tsx` — Login form (react-hook-form + zod), POST /auth/login
- `app/(app)/_layout.tsx` — Tabs navigator with auth guard (Redirect to login), FAB scan tab
- `app/(app)/index.tsx` — Dashboard: 2×2 StatusCards + scan CTA
- `app/(app)/scan.tsx` — Full-screen CameraView, EAN-8/13/QR/Code128, bottom-sheet result
- `app/(app)/stock/index.tsx` — FlatList with status filter chips
- `app/(app)/stock/[id].tsx` — Batch detail + verify action
- `app/(app)/receipt/index.tsx` — Receipt list
- `app/(app)/receipt/[id].tsx` — Receipt detail with progress bar + confirm button
- `app/(app)/inventory/[zoneId].tsx` — Zone inventory screen
- `app/(app)/profile/index.tsx` — User info + logout

## Rules followed
- SafeAreaView on every root screen ✅
- FlatList for all lists ✅
- expo-secure-store for tokens ✅
- NativeWind className only ✅
- React Query for server state ✅
- Expo Router file-based ✅
- expo-camera for barcode scan ✅
- EXPO_PUBLIC_API_URL for base URL ✅
