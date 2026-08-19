# TASK-436: Mobile ESLint and automated test infrastructure

**Date:** 2026-07-29
**Agent:** mobile-developer (Codex main session)
**Status:** done
**Duration:** implementation session

## What was done

Added an Expo SDK 56-compatible engineering verification baseline:

- ESLint 9 flat config based on `eslint-config-expo/flat`
- Jest 29 with `jest-expo` and the RN 0.85 Jest preset
- React Native Testing Library 14 and its React 19 `test-renderer`
- `npm run test` and deterministic `npm run test:ci`
- alias-aware Jest configuration and Jest types in TypeScript
- six test suites with 17 passing tests

Test coverage now includes:

- staff-session persistence and legacy cold-start restoration
- consumer-session restoration and logout cleanup
- exact lowercase role gates
- backend `storeId` to mobile `locationId` mapping
- explicit 2FA-required login failure
- paged notification response contract from TASK-427
- POS total after loyalty redemption, including over-redemption and invalid input
- a React Native shared component interaction test

The POS net-total expression was moved into a pure helper and reused by the payment screen without
changing behavior.

The new linter found a real conditional Hooks violation in the Customers list. Hooks now run in a
stable order, unauthorized queries are disabled, and navigation back occurs in an effect.

Expo Doctor follow-up:

- installed missing `expo-font` required by `@expo/vector-icons`
- removed unused direct `@react-navigation/bottom-tabs`, incompatible with Expo Router SDK 56
- aligned nine Expo/RN dependencies to the SDK 56-recommended versions
- deleted the tracked generated `.expo/README.md`; `.expo/` was already ignored

## Files changed

- `mobile/package.json`, `mobile/package-lock.json` — scripts and compatible dependencies
- `mobile/eslint.config.js` — Expo flat config, generated/build ignores, test globals, RN rule policy
- `mobile/jest.config.js` — jest-expo preset, alias, matching, timeout
- `mobile/tsconfig.json` — Jest type globals
- `mobile/app.json` — Expo added the `expo-font` config plugin
- `mobile/.expo/README.md` — removed tracked machine-generated Expo state
- `mobile/features/pos/utils/calculateNetTotal.ts` — pure POS loyalty-total helper
- `mobile/app/(app)/pos/payment.tsx` — reuse helper; no behavior change
- `mobile/features/customers/hooks/useCustomers.ts` — optional query enable gate
- `mobile/app/(app)/customers/index.tsx` — stable Hook order and safe access redirect
- `mobile/lib/__tests__/roles.test.ts`
- `mobile/features/auth/__tests__/store.test.ts`
- `mobile/features/auth/api/__tests__/authApi.test.ts`
- `mobile/features/notifications/api/__tests__/notificationApi.test.ts`
- `mobile/features/pos/utils/__tests__/calculateNetTotal.test.ts`
- `mobile/features/dashboard/components/__tests__/StatusCard.test.tsx`

Pre-existing uncommitted TASK-427 notification implementation files were not overwritten. The new
notification API test intentionally verifies their current paged-response contract.

## Decisions made

- `react/no-unescaped-entities` is disabled because React Native Text is not HTML and Ukrainian
  apostrophes do not need entity escaping.
- New React 19 compiler-oriented `purity` and `set-state-in-effect` diagnostics remain visible as
  warnings until their screens are handled in dedicated UX tasks; they are not hidden.
- Existing unrelated warnings were not mass-fixed in this infrastructure task.
- No `npm audit fix --force` was used. Its proposed remediation would downgrade Expo 56 to Expo 46
  and/or Jest Expo to an incompatible major.

## Tests

- `npm run type-check`: PASS
- `npm run lint`: PASS — 0 errors, 19 warnings
- `npm run test:ci`: PASS — 6 suites, 17 tests
- `npm ls --depth=0`: PASS
- `npx expo-doctor`: 20/21 PASS; the remaining generated `.expo` tracking check requires the
  deletion to be committed before Git reports it untracked/ignored
- Device test: not run — TASK-435 remains blocked without a device/AVD

## Dependency security

Applied non-force audit fixes. Runtime `axios` high-severity advisories were resolved.

`npm audit --omit=dev` now reports 10 moderate findings in `uuid` through Expo's Xcode/config
tooling. npm offers only `--force`, which would downgrade Expo 56 to Expo 46, so it was rejected.
The full audit additionally reports a high `brace-expansion` advisory through Jest/ESLint tooling;
its proposed force fix is also incompatible with the current Expo/Jest stack.

## Notes for next agent

TASK-437 should build on the auth-store tests added here. Add tests for refresh success, terminal
refresh failure, concurrent 401 coalescing, explicit logout, and React Query cache cleanup before
changing the interceptor.

Keep TASK-435 blocked until a physical device or AVD is available. Once available, rerun all
TASK-436 checks and the full device matrix against a fresh build.
