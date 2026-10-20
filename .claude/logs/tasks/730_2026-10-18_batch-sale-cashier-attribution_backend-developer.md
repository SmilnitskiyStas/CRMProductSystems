# TASK-730 - Batch sale cashier attribution (offline sync)

Status: review · Agent: backend-developer · not committed · extends TASK-726
- `CreateSaleRequest` += `CashierId?`, `OccurredAt?`. Batch only: foreign CashierId accepted iff (c) OccurredAt (default now) not > now+5 min / not older than 14 d, (a) user in tenant, active, POS role, same store as the token's register, (b) `register_sessions` row on THIS register with StartedAt <= at and (open or EndedAt >= at-5 min) (`ICashRegisterRepository.HadSessionAtAsync`). Otherwise item `failed` 403, nothing stored. Sale/stock events use the resolved cashier.
- Single `POST /sales`: foreign CashierId -> 400, OccurredAt ignored. Replay with an explicit CashierId different from the stored one -> 409 / failed item.
- No new migration (OccurredAt column skipped). `PosService` ctor got two optional trailing deps (users, registers).
- Tests: 10 unit (PosServiceTests) + real-Postgres A-flushed-by-B flow; full suite green.
