# TASK-082 — Mobile: "Ще" таб (More screen)

**Agent:** mobile-developer  
**Date:** 2026-06-19  
**Status:** done

## Що зроблено

Реалізовано Варіант A — "Ще" таб у bottom tab navigator.

### Зміни файлів

**Новий файл:** `mobile/app/(app)/more/index.tsx`
- SafeAreaView + ScrollView
- User info картка (аватар, ім'я, роль) з посиланням на `/(app)/profile`
- Секція "Розділи" з 5 модулями:
  - Клієнти → `/(app)/customers` (синій)
  - Service Desk → `/(app)/service-desk` (фіолетовий)
  - Розклад → `/(app)/schedules` (жовтий)
  - Списання → `/(app)/write-offs` (червоний)
  - Переміщення → `/(app)/transfers` (зелений)
- Кожен рядок: colored icon badge + label + description + chevron

**Оновлено:** `mobile/app/(app)/_layout.tsx`
- Замінено tab "Профіль" → tab "Ще" (`more/index`, `grid-outline` іконка)
- `profile/index` переведено в `href: null` (hidden, доступний через link зі "Ще" екрану)

## Результат

Tab bar: Дашборд | Залишки | Скан(FAB) | Каса | Прийомка | **Ще**

`npx tsc --noEmit` — 0 помилок.
