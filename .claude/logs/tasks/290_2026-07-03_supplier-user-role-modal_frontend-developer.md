# TASK-290 — AddTenantUserModal: role selector + success view

**Status:** done · **Agent:** frontend-developer · Updated: 2026-07-03

Попередній прогін рахував `isSupplier`/`roles`/`role`-стейт, але нічого не рендерив,
не слав `role` у бекенд, і лишив невикористані `createdUser`/`CheckCircle2`.
`TenantDetailPanel` не передавав `businessType` → `isSupplier` завжди false.

Fix:
- `types.ts`: `role: string` у `CreateTenantUserRequest`.
- `TenantDetailPanel.tsx`: `businessType={tenant?.businessType}` у `<AddTenantUserModal>`
  (рендериться до `{tenant && ...}` guard, тому `tenant` може бути undefined).
- `AddTenantUserModal.tsx`: додано поле «Роль» (select якщо `roles.length > 1`,
  read-only рядок з описом якщо один варіант — зараз завжди один); `role` тепер
  йде в `mutateAsync`; після успіху показуємо success-екран (CheckCircle2, email,
  нагадування про звичайний логін для supplier_admin, кнопка «Закрити» →
  `onCreated(createdUser); onClose()`) замість миттєвого `onClose()`.

Backend (`CreateTenantUserRequest.cs`, `ProviderService.CreateTenantUserAsync`) поки
ігнорує `role` (хардкод `EnterpriseAdmin`) — окрема backend-задача.

`npx tsc --noEmit` і `npm run build` — green, без нових помилок.
