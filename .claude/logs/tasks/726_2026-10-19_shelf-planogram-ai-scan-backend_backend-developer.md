# TASK-726 — Shelf planogram + AI shelf-photo scan (backend)

Plan: `~/.claude/plans/cheeky-juggling-tulip.md` stages 1-3 (backend only). Status: review, not committed, migration NOT applied to any DB.

## Changed
- Domain: `ShelfItem`, `ShelfScan`, `IShelfRepository`, `IShelfPhotoAdvisor`.
- Infra: EF config + DbSets, migration `20261019153635_AddShelfItemsAndScans` (tables, indexes, RLS tenant_isolation/provider_bypass/worker_bypass + RESTRICTIVE store_scope; data-migration of `itemId` from `location_zones."Position"` JSON into `shelf_items`, ExpectedFacings=1, dangling/malformed skipped, also backfills `item_zone_assignments`), `ShelfRepository`, `ShelfPhotoAdvisor` (slot Analyst, guardrail, JSON schema).
- Vision: `AiChatRequest.Images` (`AiImage`), Anthropic image blocks, OpenAI `image_url` data-URI parts. Existing callers unchanged.
- Application: `IShelfService`/`ShelfService` (planogram + scans), `ShelfDtos`; `shelf.empty` in `ValidEventTypes`.
- API: 5 endpoints in `LocationsController`, rate-limit policy `shelf-scan-ai` (10 / 3 min per user).
- Tests: `Locations/ShelfServiceTests` (mocked AI), `Infrastructure/ShelfRlsIntegrationTests` (real PG; soft-skips if DB absent or migration not applied).

## Decisions
- Image limit 5 MB (Claude API hard limit per image), not 8 MB; clients should downscale. No server-side resize (no image lib in repo).
- Failed AI call => 502, nothing persisted (no `failed` rows); raw provider error not leaked.
- `ShelfScan.AlertPending` set when missing+low > 0; worker must send `shelf.empty` and clear it.
- Photos are served via public static files (`/uploads/shelf-scans/{tenantId}/{scanId}.ext`, unguessable guid) like item images.

## Verify
`dotnet build` OK; `dotnet test` 2514/2514 (RLS test skipped-in-effect: dev DB lacks the migration).

## Follow-up (reference photos + mobile-ready API)
- `AiImage` += optional `Caption` (sent as text right before the image; Anthropic + OpenAI clients keep order: caption, image, ..., user prompt).
- `ShelfPhotoExpectedItem` += `Reference` (`ShelfReferenceImage`). `ShelfService` loads `Item.ImageUrl` only from `/uploads/items/<file>` (no sub-paths/traversal, jpg/png/webp, magic-byte check, <=1.5 MB each, <=12 images, <=8 MB total; items with images first; missing/unreadable => name/barcode only). `ShelfPhotoAdvisor.BuildImages` labels each reference ("Reference photo of item ... (id ..., barcode ...)") then the shelf photo; system prompt updated.
- New `GET .../zones/{zoneId}/shelves` -> `[{ shelfId, label, items }]`.
- Mobile contract documented in `.claude/docs/api-contracts.md` (section "Shelf planogram + AI shelf-photo scan").
- `StoreScopeRlsIntegrationTests` expected-table list += shelf_items, shelf_scans.
- NOTE: a ShelfGuard.Api process was running from this worktree and auto-applied the migration to the dev DB (RLS test now runs for real and passes). Tests were run with `-p:BaseOutputPath=<scratch>` because that process locks bin; MobileConfigSchemaContractTests fail only under that relocated output (path-relative), pass normally.
