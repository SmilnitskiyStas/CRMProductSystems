# TASK-767 — GET /api/production/analytics (backend)
- ProductionInsightsController/Service: `GetAnalyticsAsync` (same auth + RequireModule("production"), read-only, no migration). Repo: `GetAnalyticsRequestsAsync` (one split query, window = previous+current period) + `GetCompletedOrdersAsync`. DTOs `ProductionAnalyticsDto` etc.
- Extracted `ResolvePeriod` / `LocalDate` from the pipeline (contract unchanged); analytics default = last 30 days; 400 on from>to or >366 days.
- fillRate = Σ AcceptedQty of accepted-reviewed / Σ RequestedQty of ALL reviewed requests (rejected count as unfilled).
- TenantRoleTabs: `ItemProductionAnalytics` "/production/analytics", label "Аналітика" after Склад виробництва, before Рецепти.
- Tests: new ProductionAnalyticsTests.cs (9); FakeInsightsRepo extended. Release: Production+TenantRole filter 221 passed.
