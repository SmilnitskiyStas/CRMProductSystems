# Handoff: Loyalty + Marketing Analytics initiative — Фаза 0-4 complete, what's next

**From:** main session (orchestrated TASK-471..478)
**Date:** 2026-08-06
**Status:** the entire `deep-cooking-nygaard.md` initiative (Фаза 0-4) is now built, reviewed, tested,
documented, and **committed to `main`** (commit `ecf5e912`, pushed). Nothing left uncommitted for
this initiative specifically. This handoff is for whichever agent/session picks up the next piece
of work on this feature area — read this first, don't re-derive from scratch.

## Where the source of truth lives

1. **`C:\Users\stass\.claude\plans\deep-cooking-nygaard.md`** — the original plan. Фаза 0/1 are
   fully speced there; Фаза 2-4 were only a short roadmap in the plan file itself — their actual
   full specs got fleshed out during implementation and now live in `.claude/docs/decisions.md`
   (ADR-023 + 4 addendums, one per phase) and the task logs below, **not** back-written into the
   plan file itself. Read ADR-023 for the "why", not the plan file, for Фаза 2-4.
2. **`docs/uployal/*.md`** — the 4 competitive-analysis source documents this whole initiative is
   built from (`RFM_ANALYSIS.md`=Фаза 1, `PRICE_SEGMENTS_ANALYSIS.md`=Фаза 2,
   `AUDIENCE_PREPARATION_ANALYSIS.md`=Фаза 3, `AUDIENCE_ANALYSIS.md`=Фаза 4). Full UX/formula specs,
   already-catalogued competitor weaknesses this codebase deliberately fixes. Read the relevant one
   before touching that phase's code.
3. **`.claude/docs/decisions.md` ADR-023** — architecture decisions + rationale for all 4 phases,
   including two genuinely reusable lessons worth knowing before writing more code here: (a) the
   RFM engine (`RfmSegmentClassifier`/`RfmSegmentCatalog`/`IMarketingAnalyticsRepository
   .GetScoredCustomersAsync`) is meant to be called, never re-implemented — Фаза 4's migration
   matrix reuses it a third way instead of writing new classification logic; (b) the Фаза 4 XLSX
   import security story — a first fix that *looked* right (row/column count check) was empirically
   proven insufficient because the real cost was inside `new XLWorkbook(stream)` itself, before that
   check ever ran. **Any future file-upload feature must guard at the ZIP-container level
   (`ZipArchiveEntry.Length`) before the library ever parses the stream** — see
   `ExcelImportService.cs`'s own doc comment and `ImportLimits.cs`.
4. **`.claude/docs/{glossary,database-schema,api-contracts,domain-model}.md`** — current, accurate
   reference for every entity/endpoint/term across all 4 phases.
5. **Task logs `.claude/logs/tasks/404` through `478`** — the full build history. Read
   `471`-`478` for Фаза 4 specifically; `404`-`433` for Фаза 0-3.

## What's fully built and shippable (all 4 phases)

| Phase | Module key | What it does | Key files |
|---|---|---|---|
| 0 — Loyalty | `"loyalty"` | Consumer self-registration, rotating TOTP QR, POS bonus accrual/redemption | `Features/Loyalty/`, `Features/ConsumerAuth/`, mobile `(consumer)/` |
| 1 — RFM dashboard | `"marketing_analytics"` | 11-segment RFM classification, top products, affinity/cross-sell, AI recommendations | `Features/MarketingAnalytics/` (root) |
| 2 — Price segments | `"marketing_analytics"` | Median-check price tiers, frequency/reactivation audiences | `Features/MarketingAnalytics/PriceSegments/` |
| 3 — Audience builder | `"marketing_analytics"` | Product/category query builder, OR/AND, competitor audience | `Features/MarketingAnalytics/AudienceBuilder/` |
| 4 — Post-campaign | `"marketing_analytics"` | Upload a customer list, before/after campaign comparison, RFM migration | `Features/MarketingAnalytics/PostCampaign/` |

All 4 phases: `dotnet build` clean, `dotnet test` 1314/1314, `tsc`/`lint`/`next build` clean, live
E2E-tested against real seeded data, security-reviewed. Frontend routes under
`/marketing-analytics/{page,price-segments,audience-builder,post-campaign}`.

## What's explicitly NOT built (by design, not an oversight)

- **Causal/incremental analysis** (control groups, difference-in-differences, statistical
  significance) — every phase's own docs/ADR flags this as intentionally out of scope; the
  competitor itself doesn't have it either. If ever prioritized, this is a genuinely new,
  statistically-heavy piece of work, not a small addition to Фаза 4.

## Known small gaps / backlog (not blockers, worth knowing about before you touch adjacent code)

1. **KI-030 (HIGH, platform-wide, NOT this feature's own code)** —
   `TenantRole.Capabilities` never reach the JWT on login/refresh, for any tenant. Full root cause
   and 2 candidate fixes already written up in `.claude/docs/known-issues.md` KI-030 and
   `.claude/logs/reviews/bug-task476-tenantrole-capabilities-never-reach-jwt_2026-08-06.md`. A
   `spawn_task` chip already exists for this (title: "Fix TenantRole capabilities never reaching
   JWT (KI-030)") — if it's still pending, it can be started directly; if dismissed/stale, re-read
   the KI entry and start fresh. Needs security-reviewer input on fix shape before implementation.
2. **Tenant-facing Settings → Modules is still missing the `"loyalty"` key** (only
   `marketing_analytics` shows there; provider/admin panels have both). `frontend/features/modules/types.ts`.
   Small, isolated frontend fix.
3. **Consumer JWT has no revocation mechanism** (30-day lifetime, no refresh-blocklist) — flagged
   since TASK-405/412, never addressed. Would need a real design decision (blocklist store, shorter
   lifetime + refresh, etc.), not a quick patch.
4. **`consumer_self_access` RLS policy has no `FOR SELECT/INSERT` narrowing** — flagged TASK-412,
   still open. Small, contained RLS migration if picked up.
5. **`PriceSegmentSettings.MinReceiptsForBoundaries`** is persisted/settable but never read by
   `PriceSegmentsRepository.GetBoundariesAsync` — inert field, flagged TASK-422/423.
6. **No consumer-side tenant discovery** — a consumer joining a new tenant's loyalty program needs
   a raw Tenant ID today; flagged TASK-407 as needing a product decision (QR code at point of sale?
   directory search? something else?).
7. **No retention/cleanup policy for abandoned `PostCampaignSegment` drafts** — Фаза 4 is the one
   phase in this whole series with real, growing storage (segments never expire). Noted in
   decisions.md's Фаза 4 addendum as a candidate future follow-up, not filed as a KI (no live impact
   yet at this data scale).
8. **`known-issues.md` has historically been under-maintained for this feature family** — several
   Фаза 0-3 findings were flagged in task logs as "should get a KI entry" but never got one before
   this session. KI-030 (this session) is now in there; if you're doing cleanup work, the task logs
   for TASK-411/412/422/428 mention a few more that could still use a proper KI-XXX entry.

## Task ID sequencing

Last used: **TASK-478**. Next available: **TASK-479**.

## Multi-agent workflow reminder (per CLAUDE.md, already followed throughout this build)

Don't implement in the main session — spawn the appropriate role agent (`backend-developer`,
`frontend-developer`, `database-engineer`, `security-reviewer`, `documentation-writer`,
`qa-tester`), each briefed with file paths + the relevant source docs above, not vague instructions.
Schema changes always go through database-engineer first, strictly before backend-developer touches
the same entities. Security review is mandatory before anything in this module ships, matching this
whole series' own track record: **every phase without exception had at least one real bug caught by
review or QA that would have shipped silently otherwise** — don't skip that gate to save time.
