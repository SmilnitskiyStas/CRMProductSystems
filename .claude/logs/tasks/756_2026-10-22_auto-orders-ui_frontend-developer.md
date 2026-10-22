# TASK-756 frontend: auto supplier order drafts on /orders

Changed (frontend only):
- features/orders/components/AutoOrdersSection.tsx (new), hooks/useAutoDrafts.ts (new); wired into app/(dashboard)/orders/page.tsx above the manual flow (manual Generate/Confirm untouched).
- features/receipts: types (source, takenBy*, quantityOrdered in UpdateItemPayload), api (source filter, take, release, autoGenerate), hooks (source param); /receipts shows "Auto" badge on auto rows.
- notifications/types.ts: order.auto_draft + labels; messages uk/en: Dashboard.orders.page.auto.*, notifications labels.

Behaviour: cards per supplier, take/release/open-in-receipts, inline qty edit on blur (only the taker; hint otherwise), "Calculate auto orders" button, empty state.
Status: tsc --noEmit clean, eslint clean, messages JSON valid. Not checked in browser.
Notes: page is single-store (primary store), so section follows it. Release shown for taker or enterprise_admin/network_manager/provider.
