# TASK-357 — Frontend: POS cash reconciliation UI (close-shift cash count)

**Status:** done (2026-07-15) · **Agent:** frontend-developer (main session, direct implementation) · **Depends:** TASK-356

## What

Web UI for the backend cash-reconciliation contract from TASK-356
(`POST /api/pos/shifts/close` now optionally accepts `{ actualClosingCash }`).

- `frontend/features/pos/types.ts` — `ShiftDto` gained `openingCash`/`closingCash`/
  `expectedCashAmount`/`cashDiscrepancy` (all `number | null`); new `CloseShiftRequest`.
- `frontend/features/pos/api/pos.ts` — `closeShift(body?: CloseShiftRequest)`.
- `frontend/features/pos/hooks/usePos.ts` — `useCloseShift` mutation now takes an
  optional body, still invalidates `pos-shift-sales` and seeds the shift cache on success.
- New `frontend/features/pos/components/CloseShiftDialog.tsx` — replaces the old
  `window.confirm()` close flow. Optional "actual cash" number input (client-side
  `min=0` + explicit negative-value guard mirroring the backend's 400), blank = old
  no-reconciliation behavior. Surfaces server error text (e.g. the 400 message) below
  the field.
- New `frontend/features/pos/components/CashReconciliationSummary.tsx` — renders
  only when `shift.closingCash != null`. Shows opening/expected/actual cash + a
  discrepancy badge: green "Збіг" (exact), amber "Надлишок +X ₴" (surplus), red
  "Недостача −X ₴" (shortage). Wired into the existing Z-report card on
  `app/(dashboard)/pos/page.tsx`.
- `page.tsx` — both `ShiftStatusCard` "Закрити зміну" triggers now open
  `CloseShiftDialog` instead of `window.confirm`; new `closeError` state feeds the
  dialog's error prop via the mutation's `onError`.

## Bug found + fixed during verification

`CloseShiftDialog` (and, discovered while testing it, the pre-existing sibling
`OpenShiftDialog`) follow an `if (!isOpen) return null` pattern — the component stays
mounted in the parent's JSX tree, so its internal `useState` does **not** reset when
the dialog closes and reopens. Live-caught: closed a shift with `actualClosingCash=450`,
then opened+closed a second shift leaving the field blank — the dialog still showed
"450" and posted a reconciled close instead of the backward-compatible no-body close.
Fixed in `CloseShiftDialog.tsx` with a `useEffect` that resets state whenever `isOpen`
flips true. `OpenShiftDialog.tsx` has the identical latent bug (stale store/openingCash
on reopen) — left unfixed as it's a different component with no observed contract
impact here; flagged as a separate background task (`task_699b5a76`) rather than
touching a file outside this task's stated scope.

## Verification

`npx tsc --noEmit` clean (before and after the state-reset fix). Live-verified against
local dev stack (`backend-dev` + `frontend-dev`, existing logged-in session,
`ea@demo.local` / enterprise_admin):
- Opened shift with `openingCash=500`, closed with `actualClosingCash=450` →
  server response `expectedCashAmount=500, cashDiscrepancy=-50`, UI showed
  "Недостача -50.00 ₴" in `rgb(239, 68, 68)` (#ef4444, red) — confirmed via computed
  style, not just visual read.
- Opened shift with no `openingCash`, closed leaving the field blank → server
  response `openingCash/closingCash/expectedCashAmount/cashDiscrepancy` all `null`,
  Z-report renders with no "Звірка готівки" section — confirms backward compatibility.
- Opened shift with `openingCash=300`, closed with `actualClosingCash=300` → "Збіг"
  in `rgb(34, 197, 94)` (#22c55e, green).
- Surplus case (`450` actual vs `0` expected from the blank-opening-cash shift) showed
  "Надлишок +450.00 ₴" with the amber code path (same component, not separately
  re-verified by computed style since the red/green paths already confirm the branch
  logic and color wiring are correct).
- Client-side negative-value guard: typing `-10` into the actual-cash field is blocked
  by the input's native `min=0` HTML5 validation before the form's submit handler
  runs — confirmed via network log (no `POST /shifts/close` fired).

No web UI exists to create a POS sale (sales are created by the mobile app only;
`/pos` is view/open/close-only) — cash-only sales total in `expectedCashAmount` was
not exercised end-to-end with a real sale, only via the formula's `openingCash`
component (`expectedCashAmount = openingCash` when there are zero sales, which the
backend test suite from TASK-356 already covers for the sales-total branch).
