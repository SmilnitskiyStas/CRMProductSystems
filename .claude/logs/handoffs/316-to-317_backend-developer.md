# Handoff: TASK-316 (database-engineer) → TASK-317 (backend-developer)

Схема співпраці готова (див. `.claude/logs/tasks/316_2026-07-06_cooperation-schema_database-engineer.md`).

Для TASK-317 доступно:
- Entities: `SupplierAgreement`, `SupplierContractSettings`, `MarketplaceOrder(+Item)`, `SupplierSupportTicket(+Message)`; статуси — константи в `ShelfGuard.Domain.Constants`.
- Репозиторії зареєстровані в DI: `ISupplierAgreementRepository` (`GetForPairAsync` повертає live-угоду пари), `ISupplierContractSettingsRepository` (`GetByTenantAsync`), `IMarketplaceOrderRepository` (`GetByIdAsync` включає items; `CountForSupplierAsync` — для наступного OrderNumber), `ISupplierSupportTicketRepository` (`GetByIdAsync` включає messages).
- RLS: two-tenant політики — рядки видно обом сторонам; крос-тенантні читання працюють без обходів (патерн supplier_chat_sessions).
- Міграцію не застосовано — застосується на деплої/локально стандартним шляхом.

Обмеження: один live agreement на пару (partial unique index) — при повторній заявці після rejected/terminated створюється новий рядок.
