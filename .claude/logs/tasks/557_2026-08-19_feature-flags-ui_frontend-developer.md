# TASK-557 — Feature Flags UI

**Status:** done
**Agent:** frontend-developer

## What was done

Replaced the `/consumer-app/features` `PlaceholderSection` with a real screen.

- Added `frontend/features/consumer-app/components/FeatureFlagsSection.tsx` — 8 fixed `Switch`
  toggles (one per `MOBILE_CONFIG_FEATURE_KEYS`), loaded/saved via the existing
  `useMobileConfigDraft`/`useSaveMobileConfigDraft` hooks. Read-modify-write: holds the document
  minus `features` in `restOfDoc` state (mirrors `NavigationBuilderSection.tsx`'s `restOfDoc`
  pattern exactly), only `features` changes on save. Brand-new tenant gets a `buildSeedDocument()`
  copy byte-identical to `AppBuilderCanvas.tsx`'s own (all 8 flags default `false`, same as every
  other draft editor here). Plain `useState`, no react-hook-form/zod — booleans need no client
  validation, matches `BonusProgramSection.tsx`'s simpler `Switch`-only convention rather than
  `NavigationBuilderSection.tsx`'s array-validation one.
- Updated `frontend/app/(dashboard)/consumer-app/features/page.tsx` to render it instead of
  `PlaceholderSection`.
- Added `Dashboard.consumerApp.featureFlags` i18n block to both `uk.json`/`en.json`: `keys.*`
  (labels) and `hints.*` (one-line descriptions) per flag, plus `warningTitle`/`warningText` and
  the standard `saveButton`/`savingButton`/`saveSuccess`/`saveError`/`noChanges`/
  `unsavedChangesWarning` set every sibling editor screen already has.
- No `types.ts` change needed — `MobileConfigFeatures`/`MobileConfigFeatureKey` already existed
  from TASK-539.

## Labels (Ukrainian)

Програма лояльності / Акції / Каталог / Купони / Новини / Чеки / Доставка / Персональні
пропозиції — cross-checked against already-established terms in this codebase (`navTypes`/
`appBuilder.categories` in `uk.json`, and `admin.modules.loyalty`/`provider... modules.loyalty` =
"Програма лояльності") rather than invented fresh; matches the task brief's suggested wording.

## The required warning

Deliberately does **not** use the other three screens' blue `draftNotice` ("saved as draft, takes
effect after publish") — that would imply publishing matters here, and it doesn't. Instead: amber
`AlertTriangle` box (matches `TemporaryPasswordBanner.tsx`'s established "actionable warning"
convention), text grounded directly in `IConsumerFeatureFlagService.cs`'s own doc comments:

> "ці перемикачі зберігаються по-справжньому — і в чернетці, і після публікації, — але сьогодні не
> мають жодного ефекту в застосунку покупців: жоден ендпоінт клієнтського застосунку ще не
> перевіряє ці прапорці. Публікація цієї чернетки також нічого не змінить для покупців, поки цю
> перевірку не додадуть на бекенді."

Verified before writing this, not assumed: `grep`'d `IConsumerFeatureFlagService`/
`RequireConsumerFeatureAttribute` across `ShelfGuard.Api` — zero matches. Registered in DI
(`ShelfGuard.Application/DependencyInjection.cs`) and unit-tested, but no controller
(`ConsumerContentController`, `ConsumerLoyaltyController`, or any other) calls either one.

## Verification

- `npx tsc --noEmit` — clean, no errors.
- `npx next lint` (full project) — clean, no warnings/errors.
- `node -e "JSON.parse(...)"` on both `messages/uk.json` and `messages/en.json` — valid JSON.
- **No live/authenticated browser verification was performed this run** — static compile/lint
  check only. No dev server or backend was started.
- `git status` confirms the diff is limited to: `frontend/app/(dashboard)/consumer-app/features/page.tsx`,
  `frontend/features/consumer-app/components/FeatureFlagsSection.tsx` (new),
  `frontend/messages/uk.json`, `frontend/messages/en.json`. `types.ts` untouched (no new type
  needed).
