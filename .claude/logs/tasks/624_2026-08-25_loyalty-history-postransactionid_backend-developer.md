# TASK-624 — Add PosTransactionId to LoyaltyLedgerEntryDto (mobile contract gap)

**Agent:** claude-main-session · **Status:** done · **Date:** 2026-08-25

Plan: `goofy-bubbling-naur.md` (follow-up, mobile hand-off gap). Reported by the separate
mobile (Codex) team via `.claude/logs/handoffs/623-to-mobile-codex.md`'s consumer: the
"Залишити відгук" action needs `posTransactionId` per ledger entry to call
`POST /api/consumer/reviews`, but `GET /api/consumer/loyalty/{tenantId}/history` didn't
expose it even though `LoyaltyLedgerEntry.PosTransactionId` already existed.

## What changed

- `backend/ShelfGuard.Application/Features/Loyalty/Dtos/LoyaltyDtos.cs` —
  `LoyaltyLedgerEntryDto` gained a trailing `Guid? PosTransactionId` field (nullable,
  backward-compatible, matches what the mobile team requested).
- `backend/ShelfGuard.Application/Features/Loyalty/LoyaltyService.cs` — `ToLedgerDto`
  now passes `e.PosTransactionId` through. No other call site constructs this DTO
  (`new LoyaltyLedgerEntryDto(...)` — confirmed only one match repo-wide).

No entity/migration/RLS change needed — the field already existed on
`LoyaltyLedgerEntry`, this was purely a DTO-mapping gap.

## Build/test status

`dotnet build -c Release`: 0 errors, 1 pre-existing unrelated warning
(`MarketplaceServiceTests.cs:534`). `dotnet test -c Release`: **1925/1925 passing**,
zero regressions.

## Handoff

Response shape for `GET /api/consumer/loyalty/{tenantId}/history` now matches exactly
what the mobile team's contract doc specified. No further backend action needed for
this gap — mobile-side "Залишити відгук" wiring is their task, already scoped in
`.claude/logs/handoffs/623-to-mobile-codex.md`.

Committed and pushed directly (small, single-file-pair, backward-compatible field
addition — CLAUDE.md's "quick isolated fix" exception, not spawned as a separate agent).
