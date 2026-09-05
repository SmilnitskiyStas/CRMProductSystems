# TASK-696 — Supplier portal Phase 8 frontend: team performance + per-employee buyer ratings

**Agent:** frontend-developer · **Status:** review · не комічено · не задеплоєно
Backend contract: `.claude/logs/tasks/695_2026-09-03_supplier-phase8-team-performance_backend-developer.md`

## Що зроблено

### A. Supplier — team performance
- `features/supplier-cabinet/types.ts` — `SupplierEmployeePerformance`, `SupplierTeamPerformance`,
  `SupplierEmployeeReviewDetail` (field names verbatim from the backend DTOs; deltas reuse the
  existing `SupplierPeriodMetric` = `{ current, previous, percentChange }`).
- `features/supplier-cabinet/api/supplier-cabinet-api.ts` — `getTeamPerformance({from?,to?})` →
  `GET /api/supplier-cabinet/team-performance`; `getEmployeeReviews(userId)` →
  `GET /api/supplier-cabinet/team/{userId}/reviews`.
- `features/supplier-cabinet/hooks/useSupplierTeamPerformance.ts` (new) —
  `useSupplierTeamPerformance(from,to)` (key `["supplier","team-performance",from,to]`, retry:false),
  `useEmployeeReviews(userId)` (key `["supplier","team-reviews",userId]`, enabled on id).
- `features/supplier-cabinet/components/TeamPerformanceView.tsx` (new) — from/to date inputs +
  30/90/365 presets (mirrors `SupplierAnalyticsDashboard`). Shared `Table` (`minWidth 1180`,
  horizontal scroll) one row per employee: name, confirmed, shipped (+Δ), avg h→confirm, avg
  h→ship, on-time % (+Δ), discrepancy-free %, chat msgs, median response h, sessions, buyer
  rating (stars + `x.x · n`, +Δ), "Відгуки" action. `null` rate/hours → "—" (never "0%").
  `MetricDelta` renders ▲/▼ + magnitude (count / pp / rating points) from the `*Delta`
  current−previous, "—" inside an epsilon. Row click or the ghost button opens
  `EmployeeReviewsModal` (stars, comment, source badge "замовлення"/"чат", "Оцінив: {name}", date).
- `app/(dashboard)/supplier/team/page.tsx` — added local tab state; tabs "Команда" (existing
  `CabinetStaffPanel` + `RolesTab` grid) / "Ефективність" (`TeamPerformanceView`). Existing
  `SUPPLIER_ONLY` + `staff_management` guards untouched. No Sidebar change.

### B. Buyer — rate the responsible manager (delivered order)
- `features/marketplace/types.ts` — `SupplierEmployeeReviewDto`, `RateSupplierEmployeeRequest`,
  `RateChatParticipantRequest`.
- `features/marketplace/api/marketplace-api.ts` — `rateOrderManager`, `getOrderManagerRating`,
  `rateChatParticipant`, `getMyChatParticipantRatings`.
- `features/marketplace/hooks/useCooperation.ts` — `useOrderManagerRating(orderId)` (swallows
  404 → `null`, `retry:false`), `useRateOrderManager()` (invalidates that key). Key
  `["marketplace","order-manager-rating",orderId]`.
- `features/marketplace/components/RateEmployeeModal.tsx` (new) — shared rating modal (1–5
  interactive `StarRating` + optional comment), styling mirrors `ReviewModal`. Caller owns the
  mutation; `isEdit` switches the submit label.
- `app/(dashboard)/marketplace/orders/page.tsx` — `ManagerRatingRow` in the
  `order.status === "delivered"` expanded block, right after `<ReceiptDetail>`, rendered only
  when `order.confirmedByUserName` is set. Shows the manager name + existing stars & "змінити",
  or a "Оцінити" button → `RateEmployeeModal`. 400 → `toast.error(err.message)`; success →
  `toast.success`.

### C. Buyer — rate a chat participant
- hooks in `features/marketplace/hooks/useMarketplace.ts` — `useMyChatParticipantRatings(supplierId)`
  (key `["marketplace","chat-participant-ratings",supplierId]`, retry:false),
  `useRateChatParticipant(supplierId)`.
- `features/marketplace/components/SupplierChatPanel.tsx` — messages are now grouped by
  consecutive `senderUserId`; **the affordance lives in the per-sender group header** (the
  `senderName` line shown once per run, supplier side only). Not rated → a single small
  star-outline icon button (lucide `Star`, muted); already rated → the given stars + "змінити".
  Both open `RateEmployeeModal` for that `senderUserId`/`senderName`. One rating per supplier
  participant. The in-bubble name label (previously on every foreign bubble) moved into that
  grouped header — bubbles are no longer individually labelled.

### D. i18n (`messages/uk.json` + `messages/en.json`, parity verified 5021/5021)
- `Dashboard.supplierCabinet.pages.team.{tabTeam,tabPerformance}`
- `Dashboard.supplierCabinet.teamPerformance.*` (new block — range labels, all column headers,
  `deltaPp`, `hoursSuffix`, reviews-modal strings, source badges, empty/error)
- `Dashboard.marketplace.rateEmployee.*` (new shared-modal block)
- `Dashboard.marketplace.chatPanel.{rateParticipant,rateParticipantTooltip,editRating}`
- `Dashboard.marketplace.ordersPage.ordersTab.{managerRatingLabel,rateButton,editRating}`

## Перевірки
- `npx tsc --noEmit` — clean (exit 0)
- `npx next lint` (touched dirs) — "No ESLint warnings or errors"
- i18n parity — uk 5021 / en 5021, 0 diff; both files valid JSON
- `npx next build` — success (exit 0; full route table printed, `/supplier/team` 9.2 kB)

## Відхилення / нотатки
- Chat-rating affordance placed in the message-group header (not per bubble) per the brief's
  "don't clutter every bubble" — this changed `SupplierChatPanel` to group consecutive messages
  by sender and show the name label once per run.
- `RateEmployeeModal` is one shared component for B and C (title passed by caller).
- Did NOT touch backend / mobile / worker. Did not stage other sessions' dirty files
  (`layout.tsx`, `Sidebar.tsx`, `usePageTitle.ts`). Not committed.
