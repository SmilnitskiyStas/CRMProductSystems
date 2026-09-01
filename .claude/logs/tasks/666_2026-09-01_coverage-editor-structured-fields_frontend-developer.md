# TASK-666: structured per-region delivery fields in coverage editor + display panels

**Agent:** frontend-developer · **Date:** 2026-09-01 · **Status:** done (committed to main)
Frontend only. Follows TASK-665 (backend, commit de8f1632) and TASK-668 (mobile, commit 8461c226).

## Зроблено

### Types
- `features/geo/types.ts` — `DeliveryCoverageEntry` `{ regionCode, terms }` →
  `{ regionCode, deliveryDaysMin, deliveryDaysMax, minOrderAmount, note }` (all `number | null` /
  `string | null`). Mirrors backend `DeliveryCoverageEntryDto`.
- `features/marketplace/types.ts` — `SupplierCoverageForBuyer.buyerRegionTerms: string | null` →
  `buyerRegionEntry: DeliveryCoverageEntry | null` (imported from geo).

### New helper
- `features/geo/lib/formatDeliveryTerms.ts` —
  `formatDeliveryTerms(entry: DeliveryCoverageEntry, t): string`.
  `t` = `(key: string, values?: Record<string, string | number>) => string`, scoped by callers to
  `Dashboard.geo.deliveryTerms`. Builds `"1–3 дні · від 5000 грн"`: days range / from / to / exact,
  then min order amount, joined with `" · "`. Returns `""` when no structured data — callers
  substitute their own `termsByAgreement` fallback. Per-region `note` is NOT folded in — callers
  render it on its own muted line. Defensive swap of a reversed day pair; `Number.isFinite` + `>= 0`
  guard on amount.
- `features/geo/lib/formatDeliveryTerms.test.ts` — 9 cases (range, exact, from, to, amount, join,
  empty, reversed-pair, decimal-trim).

### Editor
- `features/geo/components/DeliveryCoverageEditor.tsx` — per served region row now has 3 number
  inputs (`Днів доставки: від __ до __`, `Мін. сума замовлення, грн`) + a text `Примітка` input,
  in a card with the region name as label. `setTerms` → `setEntryField(code, field, raw)`:
  empty → `null`; numbers `Number(...)` with `Number.isFinite && >= 0` guard. The 3 sub-sections
  (Обслуговувані / Не обслуговуються / Загальна примітка) are each behind a local collapsible
  `<button>` + chevron header (`useState`, all expanded by default). `CollapsibleSection.tsx` does
  not exist yet (TASK-667) — local toggle used, TASK-667 can unify.

### Display panels
- `features/marketplace/components/SupplierCoveragePanel.tsx` — served entries render
  `regionLabel` + `formatDeliveryTerms(entry, tTerms) || t("termsByAgreement")` + `entry.note` on
  its own muted line.
- `features/marketplace/components/CooperationCoveragePanel.tsx` — `served` block uses
  `data.buyerRegionEntry` → `formatDeliveryTerms` (as `termsLabel` param) + `buyerRegionEntry.note`
  line. Bottom `served.map` summary — same treatment as the profile panel.
- `DeliveryByRegionPanel` / `SupplierMetrics` (measured `RegionDeliveryStat`) left untouched.
- Profile forms (`CabinetProfileForm`, `SupplierProfileForm`) untouched — they pass the coverage
  object straight through `DeliveryCoverageEditor`'s unchanged `value`/`onChange` props.

### i18n (`frontend/messages/{uk,en}.json`)
New: `Dashboard.geo.deliveryTerms.{daysRange,daysExact,daysFrom,daysTo,minAmount}` (`{min}`/`{max}`/
`{days}`/`{amount}` params) · `Dashboard.geo.coverageEditor.{daysFromLabel,daysToLabel,
minOrderAmountLabel,regionNoteLabel,regionNotePlaceholder}`.
Removed the now-orphan `Dashboard.geo.coverageEditor.servedTermsPlaceholder` (both locales).
`Dashboard.marketplace.coverage.termsByAgreement` fallback kept.
`daysExact` is a small extension beyond the brief's 4 listed keys — covers `min === max` so it
renders `"2 дн."` instead of `"2–2 дні"`.

## Verification

- `npx tsc --noEmit` — clean
- `npx next lint` — clean
- `npx vitest run` — 59 passed / 8 files (was 50/7; +9 new formatDeliveryTerms cases)
- uk/en deep-key parity — 4621 == 4621, key sets identical
- Frontend dev (`:3001`): `/marketplace/[id]` + `(dashboard)` routes compile with 0 build errors,
  0 missing-i18n-key errors. **Could NOT verify interactively** — this repo's backend is not
  running and its port 5000 is held by an unrelated Docker container (no CORS/ShelfGuard API), so
  auth-guarded `/supplier/profile` and `/marketplace/[id]` stay on the Loading fallback. The
  running `shelfguard_staging` stack predates TASK-665 (old `buyerRegionTerms` shape) so it can't
  exercise these changes either. Same backend-availability blocker as TASK-659 / TASK-664.

## Commit

`feat(marketplace): structured per-region delivery fields in coverage editor + panels (TASK-666)`
— not pushed. Staged only the 7 in-scope files + 2 new geo/lib files; `mobile/...receiptPrinting.ts`
left unstaged.
