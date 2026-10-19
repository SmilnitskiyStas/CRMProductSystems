# TASK-718 fix review findings (backend)
- MovementRepository.GetIncomingAsync: marketplace branch += `o.ClientTenantId == tenantId`.
- ReceiptService.PostponeAsync: cap newDate <= today+365 (400 error); activity log `receipt.postponed` via IActivityLogRepository (new ctor dep, tenant-scoped, meta = dates + count only).
- MarketingAnalyticsRepository.GetNoPurchaseCustomersAsync: EXISTS on tenant's locations when storeIds non-empty -> unknown stores give [].
- ReceiptDto: trailing PostponeCount/OriginalExpectedAt/PostponedAt/DelayReason (defaults), mapped in ToDto.
- Tests: 6 new PostponeAsync tests in ReceiptServiceTests (past, >365, non-open x2, reason>500, happy path + activity log). No repo/integration test added (not cheap).
- Build OK; filtered tests 566/566 passed. API restarted; smoke: incoming=3, fake store=0, real store=5.
