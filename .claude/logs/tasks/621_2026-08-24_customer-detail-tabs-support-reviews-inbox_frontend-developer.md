# TASK-621 — Customer detail drawer tabs + `/customer-support` staff inbox (frontend)

**Status:** done · **Agent:** frontend-developer · **Updated:** 2026-08-24
Plan: `goofy-bubbling-naur.md` §4 "Картка клієнта" + "Вхідні звернень і відгуків". Handoffs read:
`.claude/logs/handoffs/618-to-frontend_backend-developer.md`,
`.claude/logs/handoffs/621b-to-frontend_backend-developer.md`. Task logs read: 616
(`CustomerSupportInboxController`), 617 (`ReviewsInboxController`). Last frontend wave of the
loyalty/support/reviews feature — all backend endpoints already built and verified (1925/1925
backend tests passing as of TASK-621b).

## Part A — Customer detail drawer tabs

`frontend/features/customers/components/CustomerDetail.tsx` restructured into 5 tabs (no shadcn
`Tabs` component exists anywhere in the repo — confirmed via search — so a small local tab-bar
was built inline matching the `tabStyle` pattern already used in `service-desk/page.tsx`, rather
than adding a new dependency):

1. **Info** — unchanged existing info card + recent transactions.
2. **Loyalty** — new `CustomerTierCard.tsx`. Renders the three distinct null-states from the
   TASK-618 handoff explicitly (not collapsed into one "empty" check): not enrolled (no
   section, just a message), enrolled/no tier yet (score shown, "no tier yet" note, no bar), top
   tier (badge shown, "top tier" note instead of a progress bar). All data comes from the
   existing `useCustomer` fetch — no new API call.
3. **Tickets** — new `CustomerTicketsTab.tsx`. Shows `openTicketCount` only (per the handoff, the
   full list lives on the separate inbox page) with a button that `router.push`es to
   `/customer-support?customerId={id}`.
4. **Reviews** — new `CustomerReviewsTab.tsx`. Renders `recentReviews[]` (already in the
   customer-detail payload, capped at 5) as star-rating + comment + reply, reusing the existing
   `frontend/features/marketplace/components/StarRating.tsx`.
5. **Profile history** — new `CustomerProfileHistoryTab.tsx` + `useCustomerProfileHistory` hook
   (`frontend/features/customers/hooks/useCustomers.ts`), `enabled` keyed to `tab === "history"`
   — lazy-fetches `GET /api/customers/{id}/profile-history` only when the tab is opened, not on
   drawer mount. `fieldName` values (`full_name`/`email`/`phone`) translated via a small lookup
   map. Empty page renders "No change history yet", not an error.

Types added to `frontend/features/customers/types.ts`: `currentTierName`/`compositeScore`/
`tierProgressPercent`/`openTicketCount`/`recentReviews` on `CustomerDetail`,
`CustomerReviewPreview`, `ConsumerProfileChange`/`ConsumerProfileChangePage`.

## Part B — `/customer-support` staff inbox

New route `frontend/app/(dashboard)/customer-support/page.tsx`, gated `AT_LEAST_STORE_MANAGER`
via the same `AccessDenied` + `hasRole` page-shell pattern TASK-620's `/consumer-app/loyalty-tiers`
uses (not service-desk's role-split rendering — this page has one audience). Wrapped in
`<Suspense>` for `useSearchParams` (mirrors `stock/page.tsx`'s existing pattern).

New feature `frontend/features/customer-support/` (`types.ts`, `api/tickets.ts`, `api/reviews.ts`,
`hooks/useCustomerSupportTickets.ts`, `hooks/useCustomerReviews.ts`,
`components/{TicketStatusBadge,TicketList,TicketDetail,ReviewList}.tsx`) — structurally mirrors
`features/service-desk/` for tickets (list + filter + detail sheet + reply + status dropdown) and
`features/supplier-cabinet/components/CabinetReviews.tsx` for reviews (rating filter + reply
block), adjusted per-domain: `ConsumerSupportTicketMessageDto` carries only `SenderConsumerAccountId`
/`SenderUserId` (no name) — `TicketDetail.tsx` resolves staff names via the existing
`features/users/hooks/useUsers` list (same source service-desk's own `TicketDetail` already uses
for its assignee dropdown) and consumer messages via the ticket's own `consumerName`. Review reply
is one-shot: `ReviewList.tsx` shows the reply read-only once `replyText` is set (no re-edit
button), a deliberate divergence from `CabinetReviews.tsx`'s editable-reply UI, matching
`ReviewService.ReplyAsync`'s 409-on-second-attempt backend behavior (TASK-617 log).

**`?customerId=` limitation (flagged per the brief rather than inventing a backend change):**
`IConsumerSupportService.GetInboxAsync(tenantId, status, page, pageSize, ct)` (TASK-616) has no
customer filter parameter. `TicketList.tsx` handles the deep link client-side: when
`customerIdFilter` is set it fetches one widened page (`pageSize=200`, the backend's own
`PagedQuery.ClampedPageSize` ceiling) instead of the normal 30-per-page, then filters the fetched
array by `customerId` — pagination controls are hidden in this mode. Correct for any realistic
per-customer ticket volume, but a customer with >200 tenant-wide tickets ahead of theirs in the
newest-first order could in theory miss older ones; noted here rather than adding a backend
param, per the brief's explicit "frontend-only this task" scope.

Sidebar: one entry added to the existing `support` group in
`frontend/components/layout/Sidebar.tsx` (re-read fresh immediately before editing, confirmed
unchanged since the earlier read this session) — `MessageCircle` icon (already imported),
`AT_LEAST_STORE_MANAGER` roles, next to `/service-desk`. TASK-620's `consumer_app` group entry
untouched.

i18n: `Dashboard.customers.detail.tabs`, `.tier`, `.tickets`, `.reviews`, `.profileHistory`, and a
new top-level `Dashboard.customerSupport` section (page/statuses/ticketList/ticketDetail/
reviewList) added to both `uk.json` and `en.json`; `Dashboard.sidebar.groups.support.customerSupport`
added to both. Validated both files parse as JSON after editing (`node -e "JSON.parse(...)"`).

## Verification

`tsc --noEmit`: clean. `npm run lint`: clean (`✔ No ESLint warnings or errors`).

Manual browser verification — started `backend-dev`/`frontend-dev` via `.claude/launch.json`
against the existing dev Postgres (`crmproductsystems-postgres-1`), logged in as an already-seeded
`network_manager` session for tenant "Свіжий Кут":

- Opened "TASK-410 Live Check Customer" (a real customer with one completed transaction, no
  loyalty membership) and clicked through all 5 tabs: Info unchanged, Loyalty correctly showed the
  "not enrolled" empty state (not a 0% bar), Tickets showed "0 / Open tickets", Reviews showed
  "No reviews yet", Profile history showed "No change history yet" — network panel confirmed the
  profile-history request fired exactly once, only after that tab was clicked (lazy-load
  confirmed), all other tab data came from the single already-cached customer-detail fetch (no
  extra requests).
- Since the dev DB had no consumer support tickets or purchase reviews for this tenant, inserted
  one of each directly via SQL (`consumer_accounts`/`consumer_support_tickets`/
  `consumer_support_ticket_messages`/`purchase_reviews`, linked to the real
  `pos_transactions` row backing the TASK-410 customer's transaction) — left in the dev DB as
  labeled fixtures ("TASK-621 verification…"), consistent with the existing convention of
  leftover named test rows already present in this DB (`Loyalty Concurrency Test Consumer`,
  `TASK-410 Live Check Customer`, `Champion One`, etc.).
- `/customer-support`: both tabs list the seeded data. Sent a staff reply on the ticket (`POST
  .../reply` → 201, thread updated with the correct staff name resolved via `useUsers`), changed
  its status to Resolved (`PUT .../status` → 200, badge updated in both the list row and detail
  header). Sent a reply on the review (`PUT /api/reviews/{id}/reply` → 200, reply now rendered
  read-only with the "Reply" button gone). Reopened the customer drawer's Tickets tab — count
  correctly dropped to 0 after the status change. Clicked "Open in inbox" from the drawer and
  confirmed it navigated to `/customer-support?customerId=...` with the ticket list correctly
  pre-filtered to just that customer's ticket; "Clear filter" correctly restored the unfiltered
  list.
- No console errors from any new code (only pre-existing benign `401→refresh` pairs on
  `/api/auth/me`, present on every route including ones untouched by this task).

Note on tooling: the `computer` tool's coordinate/ref clicks were unreliable in this session's
headless Browser pane (silently no-op on the first attempt after a fresh `read_page`, screenshot
unavailable — "Browser pane is not displayed, so the page is not compositing frames"). Switched to
`form_input` for filling text fields (reliable) and `javascript_tool` dispatching a real
`click()`/`MouseEvent` on the resolved DOM node for buttons — used purely to drive already-built
UI for verification, no application code was written via `javascript_tool`.

## Not implemented (out of scope)

Marketing-analytics tier segmentation (plan §4, explicitly "optional wave, after the ladder
already works" — not part of this task's brief). `mobile/` untouched (owned by a separate
concurrent agent per session convention).
