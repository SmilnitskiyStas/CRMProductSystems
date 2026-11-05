# TASK-768 production Overview cards + /production/analytics (frontend)
- ProductionPipeline.tsx rewritten: compact button cards (max 8, "Показати всі (N)" links), click opens existing ProductionRequestDetail modal (mode production); period selector/stats removed; window 4 = shipped today (from=to=today), link to /production/analytics.
- ProductionRequestDetail: + submitted/reviewed/shipped timestamps, "До відвантаження" link when stage=ready.
- New ProductionAnalytics.tsx + app/(dashboard)/production/analytics/page.tsx; api getAnalytics, hook useProductionAnalytics, ProductionAnalyticsDto types; Sidebar item; i18n uk+en (overview.*, analytics.*, requests.detail.*, sidebar production.analytics).
- tsc --noEmit + eslint clean. Not verified against live backend (TASK-767).
