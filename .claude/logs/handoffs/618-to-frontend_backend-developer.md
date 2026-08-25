# Handoff: TASK-618 backend-developer → frontend-developer

Plan: `C:\Users\stass\.claude\plans\goofy-bubbling-naur.md` (§4 "Картка клієнта" is this wave's
brief). Task log:
`.claude/logs/tasks/618_2026-08-24_customer-detail-tier-tickets-reviews_backend-developer.md`.

## What's ready

`GET /api/customers/{id}` (`AppPolicies.AtLeastStoreManager`, unchanged route/auth) now returns
an extended `CustomerDetailDto` — no new endpoint, no frontend round-trip changes needed beyond
reading the new fields:

```ts
{
  // ...unchanged existing fields (Id, Name, Phone, Email, Notes, Tags, TotalOrders, TotalSpent,
  // CreatedAt, RecentTransactions)...

  currentTierName: string | null;      // null = never joined loyalty OR no tier assigned yet
  compositeScore: number | null;       // null only when never joined loyalty at all
  tierProgressPercent: number | null;  // 0–100; null when no tier yet OR already at the top tier
  openTicketCount: number;             // always a number, 0 if none — never null
  recentReviews: {
    rating: number;        // 1–5
    comment: string | null;
    createdAt: string;     // ISO 8601 (DateTimeOffset)
    replyText: string | null;
  }[];                      // always an array, [] if none — never null, newest-first, capped at 5
}
```

## Null-handling notes for the UI

- `currentTierName`/`compositeScore`/`tierProgressPercent` are **not** all-or-nothing as a group
  in every case:
  - No `LoyaltyMembership` at all (walk-in, never joined) → all three null. Show "not enrolled" /
    no loyalty section, rather than a 0% progress bar.
  - Membership exists but hasn't been assigned a tier yet (new member, or the nightly
    tier-recompute job — TASK-619 — hasn't run for them / they haven't cleared the lowest tier's
    threshold) → `compositeScore` is a real number, but `currentTierName` and
    `tierProgressPercent` are both null. Worth a distinct UI state ("enrolled, no tier yet")
    rather than collapsing into the "not enrolled" case above.
  - Membership has a tier, but it's already the top rung → `tierProgressPercent` is null (no
    "next tier" to progress toward) while `currentTierName`/`compositeScore` are populated — show
    the tier badge without a progress bar (or a full/maxed one), not an error state.
- `openTicketCount`/`recentReviews` are always populated (0 / `[]`), never null — safe to render
  directly without a null-guard.

## What's NOT done (this wave's job, per plan §4/§8)

`CustomerDetail.tsx` (drawer on `/customers`) needs new tabs: rank/progress, support tickets
(count only from this endpoint — the actual ticket list lives behind the separate
`/customer-support` staff inbox, TASK-616's `CustomerSupportInboxController`, filterable by this
customer once that page exists), reviews (the `recentReviews` array from this endpoint is enough
for an inline preview; the full paged inbox is `ReviewsInboxController`, TASK-617), profile
change history (`ConsumerProfileService` / TASK-614 — not surfaced by this endpoint at all, would
need its own call if the tab wants it). The `/customer-support` staff inbox page itself and the
tier-ladder admin page are separate, already-scoped waves per plan §5 steps 7–8 — not this
handoff's concern.
