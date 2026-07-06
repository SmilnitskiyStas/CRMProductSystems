# TASK-316 — DB schema: supplier cooperation, marketplace orders, support tickets

**Agent:** database-engineer · **Date:** 2026-07-06 · **Status:** done
(Агент обірвався на session limit — лог дописано оркестратором; код агента перевірено, build green.)

## Створено

**Entities** (`backend/ShelfGuard.Domain/Entities/`):
- `SupplierContractSettings` — реквізити постачальника (legal_name, edrpou, iban, bank_name, legal_address, director_name, phone, email, service_name/description, signature_image_url, stamp_image_url, is_vat_payer). 1 рядок на supplier tenant.
- `SupplierAgreement` — заявка/договір співпраці: status `pending|approved|rejected|awaiting_signature|active|terminated`, request_message, rejection_reason, contract_number, contract_file_path, vchasno_document_id, requested/decided/signed/terminated_at.
- `MarketplaceOrder` + `MarketplaceOrderItem` — замовлення клієнта в постачальника: status `new|confirmed|shipped|delivered|cancelled`, order_number, agreement_id FK, total_amount; items зі снапшотом item_name/price/qty + денормалізовані tenant-колонки.
- `SupplierSupportTicket` + `SupplierSupportTicketMessage` — тікети підтримки клієнт→постачальник: status `open|in_progress|resolved|closed`.

**Constants** (`ShelfGuard.Domain.Constants`): `SupplierAgreementStatus`, `MarketplaceOrderStatus`, `SupplierSupportTicketStatus`.

**Repo interfaces** (`ShelfGuard.Domain/Interfaces/`) + реалізації (`ShelfGuard.Infrastructure/Data/Repositories/`):
`ISupplierContractSettingsRepository`, `ISupplierAgreementRepository` (GetForPairAsync = live agreement), `IMarketplaceOrderRepository` (CountForSupplierAsync для генерації номера), `ISupplierSupportTicketRepository`. Зареєстровані в `DependencyInjection.cs`.

**Міграція:** `20260706155440_SupplierCooperation` — 6 таблиць + RLS:
- single-tenant політика на `supplier_contract_settings`;
- two-tenant (`SupplierTenantId OR ClientTenantId`) на agreements/orders/order_items/tickets;
- EXISTS-through-parent на ticket_messages (як supplier_chat_messages);
- NULLIF-guard всюди, provider_bypass всюди; Down симетричний.
- Partial unique index на (supplier_tenant_id, client_tenant_id) WHERE status NOT IN ('rejected','terminated').

## Перевірки
- `dotnet build` — 0 warnings, 0 errors.
- Міграція НЕ застосована до жодної БД.
