---
task_id: TASK-039
date: 2026-06-12
agent: backend-developer + devops-engineer + mobile-developer
status: done
---

# TASK-039 — Telegram /start Account Linking (v1-spec §8.1)

## Flow
```
App (Профіль → Сповіщення) → POST /api/telegram/link-code [Authorize]
  → {code, deepLink: t.me/shelfguard_bot?start=CODE, expiresAt +15min}
  → opens Telegram → user taps Start
Worker getUpdates long-poll listener:
  /start CODE → validate (unused, unexpired) → users.TelegramChatId = chat.id
  → mark code used → reply "✅ Акаунт прив'язано, {ім'я}!"
  /start (no code) → onboarding instructions
```

## Files
| Layer | File |
|---|---|
| Domain | `Entities/TelegramLinkCode.cs`, `Interfaces/ITelegramLinkRepository.cs` |
| Infrastructure | migration `TelegramLinkCodes` (+RLS via users join), `Data/Repositories/TelegramLinkRepository.cs` |
| Application | `Features/Telegram/TelegramLinkService.cs` |
| Api | `Controllers/TelegramController.cs` — POST /api/telegram/link-code |
| Worker | `jobs/telegram-listener.ts` — getUpdates loop (50s long-poll, 5s backoff on errors) |
| Mobile | profile «Сповіщення» → generates code → opens deep link |
| Tests | `Tests/Telegram/TelegramLinkCodeTests.cs` — 3/3 |

## Rules
- Code: 8 chars, unambiguous alphabet (no 0/O/1/I), CSPRNG; fits Telegram
  start-payload charset [A-Za-z0-9_-]
- TTL 15 min; issuing a new code invalidates previous unused ones (single active)
- Re-linking allowed: a valid code simply overwrites TelegramChatId
- Listener is the only getUpdates consumer (one bot token, worker-only)

## Production verification
- telegram_link_codes with RLS ✓ · listener "polling…" in worker logs ✓
- POST link-code → `{code: ZP3B2HDK, deepLink, expiresAt+15m}` ✓
- Second call → first code invalidated (active=1, total=2) ✓
- Human-tap test pending user (see handoff below)

## Handoff → user
Tap to test the full loop: https://t.me/shelfguard_bot?start=CN46RYAS
(links manager@demo.local; expires 15 min after generation — regenerate from
the mobile profile if stale). Expected reply: "✅ Акаунт прив'язано, Василь Морозов!"
