# TASK-760 — split production requests: store side vs production side

- New /orders/production (store side, "Замовлення у виробництво") in Постачання; /production/requests now production-only.
- `ProductionRequestsPage` / `ProductionRequestDetail` take `mode: "store" | "production"`. Store: create/calc/edit/submit/cancel. Production: inbox (default status submitted,accepted,partial; "all" = non-draft/non-cancelled), summary tab, review/reject/ship, create orders.
- Sidebar: procurement item with item-level moduleKey "production". TenantRoleTabs.cs: ItemOrdersProduction "/orders/production" (ItemKeys + Procurement group).
- i18n uk/en. Checks: tsc, eslint clean; dotnet test TenantRole 44 pass.
