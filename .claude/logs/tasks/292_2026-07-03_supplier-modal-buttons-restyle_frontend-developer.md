# TASK-292 — Кнопки в модалках маркетплейсу під спільний стиль

**Agent:** frontend-developer · **Date:** 2026-07-03 · **Status:** done

## Problem
User feedback: кнопки в `CreateSupplierModal.tsx` («Створити постачальника») та
`AddSupplierItemModal.tsx` — raw `<button>` з inline-стилями, що не відповідали
дизайну решти застосунку.

## Fix
Замінено на спільний компонент `components/ui/Btn.tsx`:
- «Скасувати» → `<Btn type="button" variant="ghost" onClick={onClose}>`
- Primary-дія («Створити» / «Додати») → `<Btn type="submit" disabled={isPending}>`

Патерн вже використовувався в `AddTenantUserModal.tsx` — обидві модалки приведено
до нього. Змінена лише розмітка кнопок, логіка форм не торкнута.

## Verification
`npx tsc --noEmit` — clean. `npm run build` — green (40/40 pages).
