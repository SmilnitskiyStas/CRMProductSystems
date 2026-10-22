# TASK-753 — Auto-draft production requests, frontend (Stage 3)
- Form: per-line `StockHint` (useStock store_id+product_id) — on-hand qty + nearest expiry, "немає на складі" when 0.
- List/detail: `RequestSourceBadge` (Авто · буфер / Авто · акція); detail shows `autoReason` column + hint for auto drafts; edit/submit/cancel already supported for any draft.
- Page: "Розрахувати потребу" button -> POST /api/production-requests/auto-generate (store filter), feedback with count.
- Types tolerant (`autoReason?`), api `autoGenerateRequests`, hook `useAutoGenerateRequests`; uk/en messages.
- tsc clean, eslint clean. Not verified in browser; backend (TASK-752) endpoint assumed.
