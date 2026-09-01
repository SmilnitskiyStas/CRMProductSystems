# TASK-669 (QA) — verification + regression: structured delivery fields + primary supplier category

**Agent:** qa-tester · **Date:** 2026-09-01 · Covers TASK-665..668 (merged to `main`, HEAD `c2e33f62`).
Plan `eventual-whistling-rabbit.md` follow-up. Main working tree, no feature code changed.

## Verdict: **SHIP**

No blocking bugs. Every automated check green; all 9 e2e steps + every regression check PASS.

---

## Automated checks

| # | Check | Result |
|---|---|---|
| 1 | `dotnet build ShelfGuard.sln` | **0 errors**, 1 warning — CS8602 `MarketplaceServiceTests.cs:895` (pre-existing, unrelated) |
| 2 | `dotnet test ShelfGuard.sln` | **2158 / 2158 passed**, 0 failed (baseline 2158 after TASK-665) |
| 3 | frontend `tsc --noEmit` / `npm run lint` / `vitest run` | clean · clean · **59/59** (8 files) |
| 4 | uk.json / en.json deep-key parity | **4636 == 4636**, 0 drift |
| 5 | mobile `tsc --noEmit` / worker `tsc --noEmit` | clean · clean |
| 6 | `dotnet ef migrations has-pending-model-changes` | "No changes have been made to the model since the last migration." |

Watched suites all green: `DeliveryCoverageJsonTests`, `MarketplaceServiceTests`,
`SupplierAgreementServiceTests`, `ContractPdfGeneratorTests`, `ProviderServiceTests`,
`TenantAdminServiceTests`, `SupplierOnboarding*`, `DeliveryRegionsBackfillTests`,
`MarketplaceRepositoryCoverageFilterIntegrationTests`, RLS audit
`AllForceRlsTables_HaveTenantIsolationNullifGuard_ProviderBypass_AndWorkerBypass`.

---

## E2E

Stack: backend from source on `:5050` (port 5000 held by unrelated `test_2-backend-1`) against
dev DB `crmproductsystems-postgres-1`; frontend `next dev` on `:3001` with
`NEXT_PUBLIC_API_URL=http://localhost:5050`; browser pane. Backend came up clean.

| # | Step | Result | Evidence |
|---|---|---|---|
| 1 | Structured coverage editing (supplier `alpha@supplier.local`) | **PASS** | `PUT /api/supplier-cabinet/profile` 200. Stored JSON: `{"note":"Загальна примітка: графік узгоджується","served":[{"note":"Доставка Укрпоштою, самовивіз з Житомира","regionCode":"UA-18","minOrderAmount":5000,"deliveryDaysMax":3,"deliveryDaysMin":1}],"notServed":["UA-43"]}` — **no `terms` key**, nulls omitted. UI reload: editor inputs from=1 / to=3 / amount=5000 / per-region note / global-note textarea all repopulated. |
| 2 | Display (buyer `ea@demo.local`) | **PASS** | Supplier profile "Delivery regions" panel: `Житомирська  1–3 d. · from 5000 UAH` + per-region note line + `Does not deliver to: Автономна Республіка Крим` + global note. `GET /api/marketplace/suppliers/{id}/coverage?buyerRegionCode=UA-18` → `buyerRegionStatus:"served"`, `buyerRegionEntry` carries the structured fields. No override → region resolves to UA-30, status `unknown` (TASK-664 "not declared" branch). App rendered `en` locale; uk key values verified to produce exactly `"1–3 дні · від 5000 грн"`. Cooperation-modal panel verified at API + component-code level only (a live agreement for the pair hides the modal); its summary block is identical logic to the profile panel seen rendering live. |
| 3 | Contract PDF | **PASS** | Approved cooperation → downloaded `ДС-2026-001`. §5 «РЕГІОНИ ТА УМОВИ ДОСТАВКИ»: table row `Житомирська | 1–3 дні, від 5000 грн, Доставка Укрпоштою, самовивіз з Житомира`; §5.2 `Доставка не здійснюється... Автономна Республіка Крим`; §5.3 global note; §6 ПІДПИСИ СТОРІН. Correct Ukrainian, no □□□. (screenshot: scratchpad `contract.pdf`) |
| 4 | Single category at creation | **PASS** | `POST /api/provider/tenants` supplier+`medical` → 201, DB `supplier_profiles.Categories=["medical"]`; supplier+`bogus` → **400** `Unknown supplier category: 'bogus'.`; retail+`medical` → 201, field ignored, no supplier profile row. Wizard UI: "Product category" radio (4 opts) appears on picking Supplier; Next **disabled** until a category is picked, enables after; full wizard create ("QA669 UI Supplier") → DB `["medical"]`. |
| 5 | Category read-only in profile | **PASS** | `PUT /api/supplier-cabinet/profile` and `PUT /api/settings/supplier-profile` each with injected `categories:[...]` → 200; DB `Categories` stays `["auto_parts"]`, response returns `["auto_parts"]`. UI: category is read-only text ("Автозапчастини"), no checklist / TagInput. |
| 6 | Provider category edit | **PASS** | `PUT /api/provider/tenants/{id}/supplier-category`: `{"category":"auto_parts"}` → 204 + DB set; `{"category":"bogus"}` → 400 + DB unchanged; `{"category":null}` → 204 + DB cleared; retail tenant → 400 `Tenant is not a supplier.`; random guid → 404 `Tenant not found.` |
| 7 | Collapsible sections | **PASS** | All 4 sections (`General` / `Product category` / `Delivery regions` / `Schedule & payment`) open on load; header click collapses/expands (chevron flips ▾/▸, body hides). Save button outside all sections. Editor sub-sections (Served / Not served / General note) also collapsible, expanded by default. |
| 8 | Legacy `terms` back-compat | **PASS** | Raw old-shape row `{"served":[{"regionCode":"UA-30","terms":"стара умова 2-4 дні"}],"notServed":[],"note":null}` written via psql → GET (both endpoints) returns entry `note:"стара умова 2-4 дні"`, structured fields null (self-heal on read). UI-save (PUT of the healed value) → stored JSON rewritten `{"served":[{"note":"стара умова 2-4 дні","regionCode":"UA-30"}],"notServed":[]}` — **no `terms`**. |
| 9 | Mobile (static) | **PASS** | `mobile/app/(app)/marketplace/[id].tsx` — local `formatDeliveryTerms(entry)` flattens `deliveryDaysMin/Max` + `minOrderAmount` to one UA line, per-region `entry.note` rendered on its own italic line. `mobile/features/geo/types.ts` `DeliveryCoverageEntry` mirrors backend `DeliveryCoverageEntryDto`. `tsc --noEmit` clean. |

---

## Regression

| Check | Result |
|---|---|
| Region coverage filter with extra keys | **PASS** — `?regionCode=UA-18` matches alpha (served entry now has `deliveryDaysMin` etc.; jsonb `@>` subset match unaffected); `?regionCode=UA-43` → 0 (in `notServed`); unfiltered → 3 public suppliers. |
| Non-registry category string (`["dairy"]` forced on alpha) | **PASS** — supplier-cabinet profile GET, settings profile GET, marketplace list, marketplace full-profile GET, contract regenerate+download all 200, no crash. Web read-only field falls back to `categoryNone`; buyer marketplace page shows the raw key as a chip. Degrades gracefully. |
| `DeliveryCoverageBackfill -- --apply` on dev | **PASS** — runs clean, 0 rows updated / 0 categories reduced (idempotent; TASK-665's `--apply` already collapsed the one multi-category profile `b6598054…`). |
| Existing marketplace flows | **PASS** — `/marketplace/suppliers`, `/cooperation`, `/my-orders`, `/orders/awaiting-receipt`, `/geo/regions` (51 regions) all 200; cooperation request → approve → contract generate/download works end-to-end. |
| RLS audit test | **PASS** — green, no schema change (no new table/column). |

---

## Bugs / observations

**No blockers.**

- **LOW — Ukrainian plural grammar in day strings.** uk `Dashboard.geo.deliveryTerms.daysRange` = `"{min}–{max} дні"` always uses "дні"; backend `SupplierAgreementService.FormatDeliveryTerms` uses fixed `"до {n} днів"` / `"від {n} днів"`. Edge values ("до 1 днів", "1–1 дні") read slightly off. `daysExact` ("{days} дн.") covers min==max. Task's own expected string ("1–3 дні") matches current output. Impact trivial.
- **LOW — web read-only category for legacy/non-registry keys.** `CabinetProfileForm.tsx:98` / `SupplierProfileForm.tsx:74` map `categories[0]` against the 4-key registry via `.find(...)?.labelUa`; an unmatched key (existing dev suppliers hold `dairy`, `test`) renders `categoryNone` ("—") rather than the raw string. No crash; arguably the raw value should be shown. Pre-existing registry-mapping pattern, not introduced by TASK-665..668.
- **INFO — `frontend/features/geo/lib/formatDeliveryTerms.ts:61`** guards `minOrderAmount` with `Number.isFinite` only (the TASK-666 log says "+ `>= 0` guard"); backend `DeliveryCoverageJson.Validate` rejects negatives so a negative can't be persisted. Harmless.

## Dev-DB state left behind (verification artifacts)

- `alpha@supplier.local`: `DeliveryCoverage` now in structured shape (UA-18 served 1–3d / 5000 UAH / note; UA-43 not-served; global note); contract-settings populated (legal name / IBAN / etc.); category `auto_parts` (unchanged from TASK-667).
- Test tenants created: `QA669 Cat Supplier` (supplier, category cleared during step 6), `QA669 Retail` (retail), `QA669 UI Supplier` (supplier, category `medical`, made via provider wizard). Safe to leave or delete.
- Test cooperation agreement `ea → alpha` created for step 3, then **deleted** (marketplace pair unblocked).
- TASK-667's `TASK-667 Verify Supplier` (`7f126a7e…`) still present.
