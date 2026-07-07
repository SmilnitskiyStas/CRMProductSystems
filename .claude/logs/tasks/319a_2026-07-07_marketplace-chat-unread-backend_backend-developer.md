# TASK-319a — Marketplace chat: real unread tracking (backend)

**Status:** done · **Agent:** backend-developer · **Depends:** —

## What changed
- `SupplierChatSessionDto` — added `int UnreadCount = 0` (appended last, no positional
  shift for existing constructions).
- `ISupplierChatRepository` / `SupplierChatRepository`:
  - `GetSessionsAsync` tuple extended to `(Session, OtherTenantName, LastMessage, UnreadCount)`
    — unread count computed via a second `GroupBy` over `SupplierChatMessages`
    (`SenderTenantId != tenantId && !IsRead`), mirroring the existing `lastMessages` query.
  - New `MarkMessagesReadAsync(sessionId, readerTenantId, ct)` — bulk `ExecuteUpdateAsync`
    setting `IsRead = true` for the other party's unread messages in a session, same
    pattern as `ChatService.GetMessagesForProviderAsync`.
- `SupplierChatService`:
  - `GetSessionsAsync` threads `UnreadCount` through `ToSessionDto`.
  - `GetOrCreateSessionAsync` passes `0` (its return value isn't used for badge display).
  - `GetMessagesAsync` now calls `MarkMessagesReadAsync` unconditionally after fetching
    messages (after the existing access-check), so opening/polling a thread marks the
    other party's messages read. Access-denied/not-found paths return before this call.
- `ISupplierChatService` — doc comment updated to mention the mark-read side effect.

## Why no migration
`IsRead` column already existed on `SupplierChatMessage` and was simply never flipped to
`true` anywhere. `SenderTenantId` already distinguishes "my messages" vs "their messages"
per session (clean two-tenant model) — no schema change needed.

## Tests
Extended `SupplierChatServiceTests.cs` (+6 tests, mock-repo style via NSubstitute):
- new message from tenant A → `UnreadCount > 0` for B, `0` for A's own view
- `GetMessagesAsync` as B calls `MarkMessagesReadAsync(sessionId, B, ct)`; a session-list
  call for B afterward reflects `UnreadCount == 0`
- access-denied and session-not-found paths do NOT call `MarkMessagesReadAsync`

## Build/tests
`dotnet build`: 0 errors. `dotnet test`: 645/645 green (639 existing + 6 new).

## Out of scope
Tenant↔provider "Чат підтримки" (`ChatService`/`ChatMessage`) — untouched, per task scope
(needs a schema change to disambiguate sender side; separate follow-up).

## Handoff
`.claude/logs/handoffs/319-backend-to-frontend.md`
