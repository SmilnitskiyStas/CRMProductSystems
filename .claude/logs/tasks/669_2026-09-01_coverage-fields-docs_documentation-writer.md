# TASK-669 — document structured delivery-coverage fields + primary supplier category

**Agent:** documentation-writer · **Date:** 2026-09-01 · **Status:** done
Docs only. Consolidates the changes from TASK-665..668 (backend, frontend, mobile).

## Files Updated

### `.claude/docs/decisions.md`
- Updated "Updated" date to 2026-09-01
- **Added ADR-036 amendment (2026-09-01):** two follow-up refinements:
  1. Structured per-region delivery entry fields (`deliveryDaysMin`, `deliveryDaysMax`,
     `minOrderAmount`, `note`) replacing the single `terms: string`. JSON shape stays camelCase
     (no migration). Legacy self-heal on read (`terms` → `note`). No write-back of old `terms`.
     `SupplierAgreementService.FormatDeliveryTerms` flattens back to PDF's single line.
  2. One primary supplier category (0–1 entry), set at tenant creation, read-only after. Rationale:
     immutable identity fact. New provider endpoint `PUT /api/provider/tenants/{id}/supplier-category`
     to correct after the fact. Cleanup tool added to `DeliveryCoverageBackfill` for dev.
  3. Task breakdown: TASK-665 (backend), TASK-666/667 (frontend), TASK-668 (mobile).

### `.claude/docs/domain-model.md`
- Updated "Updated" date to 2026-09-01
- **Updated `SupplierProfile` section (TASK-648..668):**
  - `DeliveryCoverage`: added full shape example with structured per-region fields; documented
    nullable ranges, self-heal from legacy `terms`, no migration, no premium gate, patch semantics.
  - `Categories` (new subsection): 0–1 entry, chosen at creation via `CreateTenantRequest.supplierCategory`,
    validated only for `businessType == "supplier"`, read-only after, valid keys from
    `SupplierItemCategories` (4-category registry). Profile-update endpoints ignore any `categories`
    value. New provider endpoint for post-creation correction.

### `.claude/docs/api-contracts.md`
- Updated "Updated" date to 2026-09-01
- **Updated `SupplierCoverageForBuyerDto`:** changed `served` entries and added `buyerRegionEntry`
  field; `served: { regionCode, deliveryDaysMin?, deliveryDaysMax?, minOrderAmount?, note? }[]`;
  `buyerRegionEntry` (the matching served entry) replaces the old `buyerRegionTerms: string | null`.
- **Updated `SupplierProfileDto`:** deliveryCoverage example now shows structured fields with
  nullable amounts/days/per-region note; documented all fields as nullable.
- **Updated `SupplierProfileUpdateDto` / `CabinetProfileUpdateDto`:** reflects structured entry
  shape on the wire; added validation rules (0–365 days, ≥0 amount). Added note: `categories` field
  is now **ignored on update** (read-only, set at creation only). Preserved note on legacy
  `deliveryRegions` (still silently ignored).
- **Added `PUT /api/provider/tenants/{id}/supplier-category` endpoint:** ProviderOnly, 204/400/404,
  accepts one of the 4 registry keys or null to clear; documented as the only post-creation update
  path for supplier category.

### `.claude/docs/known-issues.md`
- Updated "Updated" date to 2026-09-01
- **Added KI-041:** legacy dev-DB supplier profiles may carry non-registry category strings
  (pre-TASK-665 data); read-only display shows the raw value; provider can correct via the new
  endpoint; new suppliers always have valid keys or null.
- **Added KI-042:** `admin/CreateTenantModal` exists but has no render site; component is correctly
  implemented and maintained but unreachable from the current app; dead code / placeholder for
  future admin page.

## Verification

All updated files:
- Preserve each file's existing heading style, ADR numbering, terseness
- Are in UTF-8 (via Edit/Write tools)
- Keep only the "Updated" date changes and the appended/inserted documentation

No code files touched. No git staging. Ready for commit.

## Commit

```
docs: structured delivery fields + primary supplier category (TASK-669)

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
```

Files staged: `.claude/docs/{decisions,domain-model,api-contracts,known-issues}.md` + this task log.
