# TASK-664 (BUG-1) — cooperation coverage panel: distinguish "region not declared" from "region unknown"

**Agent:** frontend-developer · **Date:** 2026-08-31 · **Status:** done
**Source:** BUG-1 from TASK-663 QA (`663_2026-08-31_coverage-feature-qa_qa-tester.md`). Frontend only, small fix.

## Problem
`CooperationCoveragePanel.tsx` branched only on `buyerRegionStatus`. The backend returns
`buyerRegionStatus === "unknown"` for two different cases:
- (a) `buyerRegionCode == null` — buyer region genuinely unresolved.
- (b) `buyerRegionCode != null` (e.g. `"UA-30"`) but the supplier declared it in neither
  `served` nor `notServed`.
Both rendered "Не вдалося визначити ваш регіон" + a `<RegionSelect>` asking the user to
re-pick the region already known — wrong/confusing for (b).

## Fix
`CooperationCoveragePanel.tsx`, `buyerRegionStatus === "unknown"` branch now splits on
`buyerRegionCode?.trim()`:
- truthy (case b) → single neutral line `t("regionNotDeclared", { region: regionName })`,
  no `<RegionSelect>`. Full coverage summary (served/notServed/note) still renders below via
  the unchanged `hasSummary` block. Submit never blocked (panel is advisory).
- falsy (case a) → unchanged: `regionUnknown` copy + `<RegionSelect>` override that re-fires
  the coverage query with `?buyerRegionCode=`. The `regionOverride` state/flow is untouched.

No backend / DTO change.

## i18n
New key `Dashboard.marketplace.cooperationRequestModal.coverage.regionNotDeclared` with a
`{region}` param, in **both** locales:
- uk: `Постачальник не вказав інформацію про доставку у ваш регіон ({region})`
- en: `The supplier hasn't specified delivery info for your region ({region})`
`useTranslations`, no hardcoding. uk/en deep-key parity: **4612 == 4612, 0 drift**.

## Checks
- `npx tsc --noEmit` — clean
- `npm run lint` — "No ESLint warnings or errors"
- `npx vitest run` — 50/50 (7 files)
- i18n parity (node deep-key diff) — 4612 == 4612, key sets identical

## Verification (dev: API :5000 + web :3007, dev DB, buyer `ea@demo.local` / «Свіжий Кут»)
- **Case (b), uk + en** — supplier "FE Chat Test Supplier" (null coverage), buyer primary
  `Location.RegionCode = UA-30` → panel shows the neutral line
  "Постачальник не вказав інформацію про доставку у ваш регіон (м. Київ)" /
  "The supplier hasn't specified delivery info for your region (м. Київ)", **no `<RegionSelect>`**,
  "Подати заявку" / "Submit request" stays enabled. Coverage API confirmed
  `buyerRegionCode: "UA-30", buyerRegionStatus: "unknown"`.
- **Case (a), en** — same supplier, buyer region temporarily nulled → "Could not determine
  your region" + "Select a region" `<RegionSelect>` (original behaviour). Region restored to
  UA-30 after.
- **Regression (served)** — supplier-alpha (`served UA-30`) + UA-30 buyer → "The supplier
  delivers to your region (м. Київ)" + terms + full coverage summary, unchanged.

Dev DB touched only transiently: `locations` UA-30 → NULL → UA-30 (restored to the exact
QA-663 state). `Cors__Origins` extended with `http://localhost:3007` for the API run only
(env var, not committed).

## Files
- `frontend/features/marketplace/components/CooperationCoveragePanel.tsx`
- `frontend/messages/uk.json`
- `frontend/messages/en.json`
