# TASK-673: documentation — supplier metrics history + detail page

**Agent:** documentation-writer · **Date:** 2026-09-01 · **Status:** done (committed to main)

Documentation pass for TASK-670..672: new `supplier_metrics_snapshots` table, nightly snapshot write + buyer metrics-history endpoint, and the supplier metrics detail page with trend charts.

## Зроблено

### decisions.md
- Extended ADR-036 with a 2026-09-01 amendment documenting TASK-670..672
- Decision: separate append-only `supplier_metrics_snapshots` table, not mutations to live `supplier_metrics`
- Rationale: the snapshot table has its own UNIQUE key, no concurrency-token conflict with the synchronous Rating writer
- Consequences: buyer can see metric trends; `QualityScore` stays null (no data source); charts empty until ≥2 snapshots accrue

### domain-model.md
- Added `SupplierMetricsSnapshot` entry after `SupplierMetrics`
- Described as append-only daily history, one row per (supplier, date), written by the nightly worker job
- Noted that it's read by the metrics-history endpoint for trend-chart visualization

### api-contracts.md
- Documented new endpoint `GET /api/marketplace/suppliers/{id}/metrics-history?days=[7-365]`
- Returns `SupplierMetricsHistoryPointDto[]` (oldest→newest), clamped days default 90
- Noted limitation: charts empty until ≥2 snapshots; QualityScore permanently null
- Cross-tenant read uses `IProviderRlsOverride` (ADR-035 pattern)

### database-schema.md
- Verified TASK-670 section already present (table structure, unique index, RLS policies, migration)
- No changes needed — section complete from TASK-670 commit

### known-issues.md
- Added KI-043: metric-history trend charts empty until ≥2 daily snapshots accrue (~2 days after deploy)
- Clarified `QualityScore` null permanently (no data source)
- Cross-referenced KI-038 (structural measurement limitations, different issue)

## Verification
- All files updated with ISO 2026-09-01 date
- Consistency pass: domain-model / decisions / api-contracts / known-issues align
- No duplicate KI numbers: KI-043 is new; KI-042 remains CreateTenantModal
- Format matches existing patterns (headings, code blocks, cross-references)

## Commit
`docs: supplier metrics history + detail page (TASK-673)` on `main` (not pushed).

---

## Summary

**Files updated:** decisions.md, domain-model.md, api-contracts.md, known-issues.md

**Key decisions documented:**
- ADR-036 amendment explains separate append-only snapshot table design
- Snapshot table is a distinct entity, no concurrency risk with live metrics
- Charts are empty until ≥2 points; QualityScore null (no data source)

**Cross-references wired:**
- API contracts link to ADR-035 (provider RLS override pattern)
- Known issues reference ADR-036, KI-038 (related measurement limitations)
- Domain model connects snapshot table to history endpoint and detail page

**Ready for:** merge to main; no outstanding documentation gaps for TASK-670..672.
