# 293-arch — Supplier Categories & Provider Nav Split (design)

**Status:** done · **Agent:** project-architect · Date: 2026-07-03

Designed two features on top of v4.1 (ADR-016): provider nav split (Клієнти/Постачальники)
and per-item supplier categories with dynamic attributes.

**ADR-017** added to `.claude/docs/decisions.md`:
- Feature A: extend existing `/provider` tab state (`"clients"|"suppliers"|"logs"`),
  client-side filter over already-loaded `useTenants()` list by `businessType`. No new
  route, no new endpoint.
- Feature B: `SupplierItem` gets nullable `category` (string) + `attributes` (JSONB dict) —
  chosen over fixed per-category columns to avoid migration-per-category. Category/field
  registry lives in backend (`SupplierItemCategories` const), exposed via
  `GET /api/marketplace/item-categories`, so validation of required fields (e.g. medical
  needs expiry_date) happens server-side, not just in the React form. Items without a
  category remain permanently valid (not a migration transient state).

**New sprint** `v4.2 Supplier Categories & Navigation` in `.claude/tasks/current.md`:
TASK-293 (DB migration) → TASK-294 (category registry + endpoint, backend) → TASK-295
(DTOs + CRUD validation, backend) → TASK-296 (dynamic item form, frontend) → TASK-297
(provider tabs split, frontend, no deps) → TASK-298 (QA regression).

No code written (architecture only, per role guardrails).
