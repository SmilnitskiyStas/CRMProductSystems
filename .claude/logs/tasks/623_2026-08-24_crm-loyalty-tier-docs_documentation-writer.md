# TASK-623 — CRM/loyalty tier expansion: documentation pass

**Agent:** documentation-writer · **Status:** done · **Date:** 2026-08-24

Plan: `C:\Users\stass\.claude\plans\goofy-bubbling-naur.md`. Read all of TASK-613..622 (+621b) task
logs and the 613/615/618/621b handoffs before writing. Docs-only — no code touched, per the brief.

## What changed

- **`database-schema.md`** — new section for the 6 tables (`consumer_account_profile_changes` no
  RLS, `loyalty_tier_definitions`, `loyalty_tier_change_history`, `consumer_support_tickets`,
  `consumer_support_ticket_messages`, `purchase_reviews`), RLS policy table, the
  `LoyaltyMembership`/`PosTransaction.CashRegisterId` extensions, and the EF phantom-FK bug
  (`HasOne<T>().WithMany().HasForeignKey()` vs. `HasOne(x => x.Nav).WithMany()`) worth remembering
  for future entities with both a scalar FK and a nav property.
- **`domain-model.md`** — extended the existing `LoyaltyMembership` entry (tier fields + the
  nightly-only-write rule) and added 6 new entity sections + a "Relationships" subsection before
  `## Key Business Rules`, plus a new rule #7 (tier score is worker-job-only, mirrors rule #3's
  "computed by a job, never live" shape).
- **`api-contracts.md`** — 6 new sections: Consumer Profile, tier ladder (admin CRUD + consumer
  read), support tickets (consumer + staff inbox), reviews (consumer + staff inbox), customer
  detail extension + profile-history endpoint. DTO/request shapes pulled from the actual DTO record
  files (`ConsumerProfileDtos.cs`, `ConsumerSupportDtos.cs`, `ReviewDtos.cs`, `LoyaltyDtos.cs`) and
  controllers, not re-derived — task logs gave routes/behavior but not always exact JSON field
  names, so I read the source records directly for those.
- **`decisions.md`** — new ADR-034, 6 decisions (phone-change verification, per-item tier discount,
  composite-score formula/timing, worker-job write boundary, review-ownership resolution path,
  ticket/review pattern reuse over ServiceDesk).
- **`known-issues.md`** — new KI-034 (`/customer-support?customerId=` client-side page-widening
  limitation, low severity, not a blocker — matches TASK-621/622's own characterization).
- **`glossary.md`** — two new terms in the existing Loyalty & Marketing Analytics section: "Tier
  ladder (рангова драбина)" and "Composite score."

All six files' `**Updated:**` headers bumped to 2026-08-24.

## Mobile hand-off

Wrote `.claude/logs/handoffs/623-to-mobile-codex.md`, following the `586-to-mobile-codex.md`
precedent (the only existing example of this repo handing a finished backend/web feature to the
separate mobile Codex agent). Curated extract of the 4 consumer-facing endpoint groups
(profile/tier/support/reviews) with DTO shapes copied from `api-contracts.md`, plus pointers at
existing mobile screens (`account.tsx`, wallet/loyalty screen, `history.tsx`) as natural extension
points and the "no push notifications, no camera/scanner needed" scope note. Kept shorter than
TASK-586's handoff — this feature is forms + lists, not a camera/barcode flow.

## Not done

No code changes (task scope was docs-only, QA already confirmed everything works). Did not touch
`mobile/`.
