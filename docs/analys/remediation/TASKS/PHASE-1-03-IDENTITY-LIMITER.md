# PHASE-1-03-IDENTITY-LIMITER

- **Source ID / severity:** SEC-012 / P2; CONFIRMED перед fix.
- **Root cause:** UseRateLimiter виконувався раніше за UseAuthentication, тому subject policies бачили IP fallback.
- **Final change / evidence:** backend/ShelfGuard.Api/Program.cs:416–419: Authentication → RateLimiter → Authorization. Наявні anonymous IP policies і endpoint metadata збережені; quotas/forwarded trust не змінені (SEC-011/013 окремо).
- **Tests:** backend build PASS; auth/authorization regressions у suite 541 PASS. Статично перевірено actual middleware registration order. Повний ST-13, тобто два users за одним IP і один user з двох IP на реальному HTTP pipeline, NOT TESTED; не оголошувати цей runtime gate закритим.
- **Migration:** не потрібна. Rollback: лишити authentication перед subject limiter. Owner approval: продовження фази 1.
- **Status:** LOCAL PATCH + BUILD/REGRESSION VERIFIED; ST-13 OPEN; NOT DEPLOYED.
