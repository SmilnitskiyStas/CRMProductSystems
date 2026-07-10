# TASK-333 — Landing lead capture endpoint (backend-developer, 2026-07-10)

**Status:** done

## Contract (fixed, frontend builds against it)
`POST /api/public/leads` — `[AllowAnonymous]`, `[EnableRateLimiting("public-leads")]` (5 req/min per IP, fixed window, same pattern as `auth-login`).
Body: `{ name (2..100, required), phone (5..30, required), company? (≤150), message? (≤1000), website? }`.
- `website` = honeypot → non-empty ⇒ 204 without saving.
- Success ⇒ 204 NoContent. Validation ⇒ 400 `{ "error": "..." }`.

## Files
- `ShelfGuard.Domain/Entities/LandingLead.cs` — new entity (Id, Name, Phone, Company?, Message?, Source="landing", IsProcessed=false, CreatedAt UTC)
- `ShelfGuard.Domain/Interfaces/ILandingLeadRepository.cs`
- `ShelfGuard.Infrastructure/Data/Repositories/LandingLeadRepository.cs`
- `ShelfGuard.Infrastructure/Data/AppDbContext.cs` — DbSet + config: table `landing_leads`, **no tenant_id / no RLS** (provider-level, same as `provider_roles` / `provider_schedule_slots`); index on CreatedAt
- `ShelfGuard.Application/Features/Leads/` — `CaptureLeadRequest` (all-nullable DTO → validation in service, keeps `{error}` contract), `ILandingLeadService`, `LandingLeadService` (honeypot, validation, trim/normalize, save, `ILogger` info per saved lead)
- `ShelfGuard.Api/Controllers/PublicLeadsController.cs` — thin controller
- `ShelfGuard.Api/Program.cs` — `public-leads` rate limit policy
- DI: Application + Infrastructure DependencyInjection

## Migration
`20260710112137_AddLandingLeads` — additive only (CreateTable landing_leads + IX_landing_leads_CreatedAt).

## Telegram notification
Skipped: worker notification pipeline is tenant-scoped (resolves recipients by TenantId + role) — provider-level message needs a recipient convention/schema change. TODO left in `LandingLeadService`. DB row is the source of truth.

## Verification
- `dotnet build` — 0 errors
- `dotnet test` — 701/701 passed (16 new in `ShelfGuard.Tests/Leads/LandingLeadServiceTests.cs`: honeypot skips save, validation bounds, happy path, trim/null normalization)
