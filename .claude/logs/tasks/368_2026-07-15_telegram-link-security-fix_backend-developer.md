# TASK-368 — Fullstack: fix unverified Telegram account-linking path (security)

**Status:** done (2026-07-15) · **Agent:** backend-developer (main session, direct — no sub-agent
per explicit instruction) · **Depends:** TASK-367 finding

## Context

TASK-367 (Block 15 audit) flagged two competing Telegram-linking mechanisms:
1. `POST /api/auth/telegram/link` (`AuthController`) — frontend (`TelegramLinkSection.tsx`) let
   a user paste a raw numeric Telegram chat_id directly, with **zero proof of ownership**. Anyone
   who knew/guessed another chat_id could link it to their own account and receive that chat's
   notifications, or "subscribe" a chat that never opted in.
2. `POST /api/telegram/link-code` (`TelegramController`/`ITelegramLinkService`) — generates a
   one-time 15-min code + deep link `t.me/<bot>?start=<code>`; the worker's
   `telegram-listener.ts` validates `/start <code>` and writes `users.TelegramChatId` itself —
   proof of ownership by construction. Frontend never called it; `telegram_link_codes` was always
   empty in practice.

User confirmed directly in chat: fix now — wire up the safe code-based flow, remove the unverified
direct path.

## Investigation

- `TelegramLinkService.CreateLinkCodeAsync` returns `LinkCodeDto(Code, DeepLink, ExpiresAt)` —
  8-char code (unambiguous alphabet), 15-min TTL, invalidates the user's previous unused codes.
- `worker/src/jobs/telegram-listener.ts`'s `handleStart` was **already fully correct**: long-polls
  Telegram `getUpdates`, on `/start <code>` looks up `telegram_link_codes` (unused, unexpired),
  writes `users."TelegramChatId"`, marks the code used, sends a confirmation message. Sets
  `app.role = 'worker'` correctly (Block 2 RLS fix). No changes needed here — confirmed per the
  brief's "if it needs no changes, just confirm" instruction.
- Mobile (`mobile/app/(app)/profile/index.tsx`) **already uses the safe flow** —
  `apiClient.post('/telegram/link-code')` + `Linking.openURL(deepLink)`. Only the web frontend
  used the unverified endpoint. No mobile changes needed.
- v1-spec.md §8.1 always specified the code-based flow (`t.me/BotName?start=CODE`, generated in
  profile) — the raw-chatId endpoint was an implementation deviation from spec, not a deliberate
  simpler design. Updated the terse §5 endpoint listing (line 597) to match reality.
- **Found a second, previously-unknown bug while wiring the new UX**: `AuthUserDto` (returned by
  `GET /api/auth/me`, used by `useMe()`) never had a `TelegramChatId`/`HasTelegram` field at all.
  The old "Telegram: Підключено" status shown in `TelegramLinkSection.tsx`/`UserProfileCard.tsx`
  was driven **entirely** by a client-side optimistic React Query cache patch
  (`useLinkTelegram`'s `onSuccess` setting `telegramChatId: "linked"` locally) — never real server
  state. Any full reload, logout/login, or any of the app's many `invalidateQueries(ME_KEY)` calls
  elsewhere would have silently reverted the displayed status to "not linked" even though
  `users.TelegramChatId` was genuinely set in the DB. This blocked the new polling-based UX
  outright (polling `/api/auth/me` would never see the field change), so fixing it was required,
  not optional scope creep.

## Changes

**Backend:**
- `AuthController.cs` — removed `POST /api/auth/telegram/link` entirely; left a comment
  explaining why and pointing at the replacement flow.
- `IUserService.cs` / `UserService.cs` — removed `LinkTelegramAsync` (zero test coverage,
  confirmed via grep before deleting).
- `UserDtos.cs` — removed `LinkTelegramRequest`.
- `AuthDtos.cs` / `AuthService.cs` — added `TelegramChatId` to `AuthUserDto`, wired from
  `User.TelegramChatId` in `ToDto`. This is the fix that makes real linking status actually
  observable via `/api/auth/me`.
- `v1-spec.md` §5 — corrected the stale endpoint listing.
- `User.LinkTelegram(string)` domain method left in place (still a reasonable one-line
  encapsulated setter API on the entity; only its one insecure caller was removed).

**Frontend (`features/profile/`):**
- `types.ts` — replaced the unused/wrong `TelegramLinkResponse` (`linkUrl`) with
  `TelegramLinkCodeResponse` (`code`/`deepLink`/`expiresAt`, matches the real `LinkCodeDto` wire
  shape).
- `api/profile.ts` — removed `linkTelegram(chatId)`; added `createTelegramLinkCode()` →
  `POST /api/telegram/link-code`.
- `hooks/useProfile.ts` — removed `useLinkTelegram`; added `useCreateTelegramLinkCode()`.
- `components/TelegramLinkSection.tsx` — rewritten. New flow: "Згенерувати код" → shows the
  8-char code (copyable) + "Відкрити в Telegram" deep-link button + manual-fallback instructions
  + a live "Перевірити зараз" button + automatic 3s polling (`invalidateQueries(ME_KEY)`, matches
  the codebase's existing polling convention used by chat/marketplace/IoT hooks) that detects
  `me.telegramChatId` becoming set and switches to the success state on its own — no page reload
  needed. Expired-code state (15 min) shows a clear message + lets the user regenerate.

## Verification

- `dotnet build` — 0 errors (1 pre-existing unrelated warning in `MarketplaceServiceTests.cs`).
- `dotnet test` — 850/850 green, no failures (removed members had zero existing test coverage).
- `npx tsc --noEmit` — clean.
- Live end-to-end on the local dev stack (frontend :3000 + backend :5000 + dev Postgres):
  logged in as the demo user, generated a code via the real UI → confirmed
  `POST /api/telegram/link-code` returned 200 with a real code + `https://t.me/shelfguard_bot?start=<code>`
  deep link. Since no real Telegram bot session exists in this environment, simulated the
  worker's exact side effect (`UPDATE users SET "TelegramChatId" = ... WHERE ...`, same statement
  `telegram-listener.ts` runs after a valid `/start <code>`) directly against the dev DB via
  `docker exec crmproductsystems-postgres-1 psql`. Confirmed the running UI **automatically**
  flipped from "Не підключено" + pending-code view to "✓ Підключено" within one poll cycle, with
  no manual action — proof the polling loop and the new `AuthUserDto.TelegramChatId` field work
  together correctly. Also confirmed the status **survives a hard page reload** (real server
  state now, not the old cache-only fiction). Reset the demo user's `TelegramChatId` back to
  `NULL` afterward to leave dev DB clean.
- Checked dev DB for any pre-existing rows linked via the old insecure endpoint
  (`SELECT ... WHERE "TelegramChatId" IS NOT NULL`) — **0 rows**, nothing to document/migrate.

## Not changed (confirmed correct as-is)

- `worker/src/jobs/telegram-listener.ts` — no changes needed, already implements the secure
  `/start <code>` flow correctly end-to-end.
- Mobile app — already used the safe `/telegram/link-code` flow exclusively.

## Minor gap flagged, not fixed

Linking via the worker's raw-SQL path writes no `activity_logs` row (the only place that ever
logged `user.telegram_linked` was the now-removed `UserService.LinkTelegramAsync`). Before this
fix the audit trail "worked" only for the insecure path; after this fix, real linking never
appears in the user's activity log. Small, low-severity, out of scope for this security fix (would
require the worker to write to `activity_logs` via raw SQL, a new touch point not requested).
Candidate for a small dedicated follow-up if the activity log is expected to be exhaustive.
