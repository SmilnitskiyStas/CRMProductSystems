# TASK-351 — Security audit Block 1: Auth & Access Control

**Status:** done (2026-07-14) · **Agent:** security-reviewer (main session, per user instruction — no sub-agent spawn) · **Depends:** TASK-350 (Block 0)

Block 1 of the pre-launch audit (`C:\Users\stass\.claude\plans\eager-pondering-tower.md`).
Scope: `backend/ShelfGuard.Application/Features/{Auth,Users,TenantRoles}`, controllers,
`Infrastructure/Authorization/*`, v1-spec.md §3 (role matrix), ADR-019/020 (decisions.md).

## Functional / code review findings

**No P0/P1 found.** The Auth/Users/TenantRoles stack has already been through several
recent hardening passes (TASK-329/330 auth hardening + 2FA, TASK-346/347 ADR-020
escalation-gap fixes) — reviewed and confirmed still correct:

- Login → refresh rotation → logout: refresh tokens hashed at rest, rotation on every
  refresh, **reuse detection** revokes the whole token family + logs
  `auth.refresh_reuse_detected` (`AuthService.cs:99-121`). Refresh cookie is `HttpOnly`,
  `SameSite=Strict`, `Secure` (except localhost) — checklist item confirmed.
- Lockout: 5 failures → 15 min, shared counter with 2FA failures, generic error (no
  enumeration), audited (`user.login_failed`/`user.locked_out`) — already unit-tested.
- Password policy: 12+ chars, letter+digit, ~100-entry common-password blocklist,
  email-local-part rejection (`PasswordValidator.cs`) — applied at all 5 set-password
  sites per TASK-329.
- 2FA TOTP: setup/enable/disable/recovery-codes flow correct; anti-replay via
  last-accepted-timestep; recovery codes SHA-hashed, single-use.
- Role matrix (v1-spec §3.2) vs `AppPolicies.cs`: matches for view/analytics/receiving/
  transfers/discounts. One **divergence** (informational, not a vulnerability): spec
  grants "staff management" to network_manager and store_manager; current code only
  lets `store_manager`+ *update* users (`StoreManagerOrUsersManage`) but restricts
  *invite/deactivate* to `enterprise_admin`+ (`EnterpriseAdminOrUsersManage`) — narrower
  than spec, looks deliberate (matches the ADR-020 doc comment) but flagging since it's
  a spec/code mismatch — no code change made, needs a product call (loosen to match
  spec, or update v1-spec.md to reflect the tightened rule).
- ADR-019 temporary grants / ADR-020 TenantRole capabilities: verified these are real
  backend enforcement (JWT claims baked in at mint time, `RoleOrCapabilityHandler`
  OR'd onto existing role policies), not UI-only — confirmed by reading
  `AuthService.BuildEffectiveCapabilitiesAsync`/`BuildEffectivePermissionsAsync`,
  `RoleOrCapabilityHandler.cs`, `TenantRoleAuthorization.cs`, and the escalation-gate
  tests in `UserServiceEscalationTests.cs`.
- Impersonation: `ProviderController` is `[Authorize(Policy=ProviderOnly)]` class-level
  (provider role only); `ProviderService.ImpersonateAsync` logs `provider.impersonate`
  to `activity_logs` with tenant/provider email before minting the token — checklist
  item confirmed.
- `[Authorize]`/`[AllowAnonymous]` audit: `UsersController` and `TenantRolesController`
  already had explicit policy attributes on every action. `AuthController`'s public
  endpoints (`login`, `2fa/verify`, `refresh`) relied on the *absence* of any auth
  attribute (functionally anonymous today since no global `FallbackPolicy` is
  configured, but not self-documenting/audit-proof) — **fixed**: added explicit
  `[AllowAnonymous]` to all three actions.
- tenant_id is read from JWT claims everywhere in this scope (`User.FindFirst("tenant_id")`),
  never from request body — confirmed across all three controllers.

## KI-005 fix — hardcoded bcrypt hash removed

`DbSeeder.SeedAsync` now takes `IPasswordHasher hasher` + optional `IConfiguration config`
and hashes the demo password at runtime: `config["Seed:DefaultPassword"]`, falling back to
`"password"` only when unset (documented dev-only default). `Program.cs` resolves
`IPasswordHasher` via `scope.ServiceProvider.GetRequiredService<...>()` and passes
`app.Configuration`. No hardcoded hash remains in source (git history still has it —
out of scope to purge history). Still gated to Development/`SEED_ON_START=true` (KI-006),
so this path never runs unattended in production regardless.
`.claude/docs/known-issues.md` KI-005 marked resolved.

## New tests

`backend/ShelfGuard.Tests/Users/UserServiceCrossTenantTests.cs` (5 tests) — basic
cross-tenant guard: a caller resolving to tenant A cannot read/update/deactivate/view
activity/assign a TenantRole for a user belonging to tenant B by reusing that user's id.
Pins the existing `target.TenantId != tenantId → "User not found."` guard present in
every `UserService` method in scope. Lockout-after-5-failures and 2FA already had test
coverage (`AuthServiceTests.cs`, `TwoFactorAuthTests.cs`) — no new test needed there.

**Not added — left as TODO for Block 2/18:** an HTTP-level "protected endpoint without
token → 401" test. This repo has no `WebApplicationFactory`/integration-test harness at
all (only NSubstitute-backed service unit tests) — building one is a real chunk of new
test infrastructure, out of scope for "critical scenarios only, no full coverage" per
the agreed audit depth. The 401 behavior itself is ASP.NET Core's built-in
`[Authorize]` middleware, not custom code, so regression risk is low; still worth
picking up when Block 18 (security/pentest) or Block 2 (RLS cross-tenant HTTP sweep)
stands up an integration-test harness.

## Build/tests

`dotnet build`: 0 errors, 0 warnings. `dotnet test` (full suite): **805/805 green**
(5 of those are the new `UserServiceCrossTenantTests`; the rest were already green
and unaffected by this task's changes).

## Needs a user decision

The role-matrix divergence above (staff invite/deactivate narrower than v1-spec §3.2
for network_manager/store_manager) — no code change made pending a product call.
