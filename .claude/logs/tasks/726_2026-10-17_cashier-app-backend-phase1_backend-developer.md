# TASK-726 - Cashier app backend, Phase 1 (registers, PIN sessions, idempotent sales, delta catalog)

Status: review · Agent: backend-developer · not committed/pushed · Plan: ~/.claude/plans/tranquil-singing-barto.md

## What changed
- Domain: `CashRegister`, `RegisterSession`; `PosShift.RegisterId`; `PosTransaction.ClientTransactionId` (+ FK on existing `CashRegisterId`); `User.PosPinHash/PosPinSetAt`; `Item.UpdatedAt`.
- Migration `20261017144512_AddCashRegisters` (generated, SQL appended): RLS on `cash_registers`/`register_sessions` (FORCE, tenant_isolation w/ NULLIF + provider_bypass + worker_bypass); `items.UpdatedAt` backfill + trigger `items_set_updated_at`; unique `(TenantId, ClientTransactionId)` partial index; one open shift per register (store-level unique narrowed to `RegisterId IS NULL`). Applied to dev DB.
- `Features/Registers`: `RegisterService` (CRUD, activation code, revoke, PIN set/clear, PIN session, end session), `PosSyncService` (delta catalog), `RegisterCredentials`.
- `RegistersController` (`api/pos/registers`): manager endpoints `AtLeastStoreManager` + `[RequireModule("pos")]`; anonymous `activate` and `session` (rate-limit policies `register-activate` 10/min, `register-session` 20/min per IP); `session/end` needs operator JWT.
- `PosController`: register-aware shift open/current/close/sale via `register_id` claim; `POST sales/batch`; `GET sync/catalog?since=&afterId=&limit=`.
- `PosService`: `ClientTransactionId` idempotency (pre-check + unique-index backstop -> `DuplicateClientTransactionException`), `CashRegisterId` stamped, `CreateSalesBatchAsync` (max 100, per-item created/duplicate/failed, no inline fiscalization, `DiscardPendingChanges` after a failed item). New optional trailing `registerId` params; `IPosRepository` + 3 members.
- `IJwtService.GenerateOperatorToken` (role fixed `cashier`, claims register_id/cashier_id/session_id/store_id; `Jwt:OperatorTokenMinutes`, default 480).

## Design decisions (not in brief)
- Device token `sgr1.{tenantN}.{registerN}.{secret}` and activation code `{tenantN}.{XXXX-XXXX}` embed the tenant id so the anonymous endpoints can open a tenant scope via `ITenantSessionOverride` (no RLS weakening); only SHA-256 of the secret/code is stored. Activation is one atomic conditional UPDATE (single-use, 24 h TTL).
- PIN: bcrypt (existing hasher); PIN-only sign-in scans the store's PIN users; optional `cashierId` for badge/pick-list. Lockouts: per user `FailedLoginAttempts/LockoutUntil` (5 -> 15 min), per register (PIN-only, 10 -> 5 min; new `FailedPinAttempts/PinLockoutUntil`).
- Delta catalog needs a change marker: `items.UpdatedAt` via DB trigger; cursor `(updatedAt, id)`.

## Verify
`dotnet build` clean; `dotnet test` 2527/2527 (new: RegisterServiceTests, PosServiceTests additions, RegisterFlowIntegrationTests on real Postgres incl. policy-shape check).

## Known gaps / follow-ups
- Operator must have a `user_locations` row for the store (store_scope RLS on pos_*), as for any cashier.
- Sale replay after the shift is closed returns 409 (offline-after-close handling is Phase 4); deleted items are not in the delta feed.
- No returns / per-register Checkbox / frontend yet. `openapi.json` not regenerated.

## Security-review hardening (same task, migration `HardenCashRegisters`, applied to dev)
- Register PIN guard: atomic `UPDATE ... RETURNING` failure counter, 10 per 15 min window (decays), never reset by success; 50/UTC day -> register status `locked`, manager `POST registers/{id}/unlock`. Applies to PIN-only and cashierId sign-ins.
- PIN failures use own `User.PosPinFailedAttempts/PosPinLockoutUntil`; password lockout never touched/cleared.
- `SetPin` now server-generated (6 digits, returned once, silent collision retry, `POST registers/pins/{userId}`, rate-limited); target must hold a POS role, not outrank caller, and be in a store the caller manages (store_manager own store, network_manager `user_locations`, enterprise_admin tenant-wide). Sign-in requires active user + POS role; PIN-only capped at 30 candidates.
- Register management (list/create/update/code/revoke/unlock) store-scoped the same way; no moving a register to a foreign store.
- Operator token: audience `<aud>:register`, accepted only on `[AllowOperatorToken]` endpoints (PosController, 3 POS Loyalty actions, session/end); JwtBearer `OnTokenValidated` checks open session + active register + active POS user + `pos` module in the token tenant scope (30 s cache, invalidated on end/replace/revoke). TTL kept 8 h. `PosController` now `[RequireModule("pos")]`.
- Activation: code checked before module state, one identical 400 for every failure.
- Idempotency: replay must match register (if known), shift, payment type and items, else 409.
- `items_set_updated_at` uses `clock_timestamp()`; DTO docs tell clients to resume with an overlap.
- Tests: 2549/2549 (incl. real-Postgres atomic counter + validator lifecycle).
