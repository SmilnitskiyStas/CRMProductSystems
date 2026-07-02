# TASK-280 — Dashboard: обмеження блоку «Потребують уваги» + «Переглянути всі»

**Agent:** frontend-developer · **Date:** 2026-07-02 · **Status:** done

## Problem
`AttentionTable` на дашборді рендерив усі attention-товари без обмеження —
при великій кількості товарів блок займав пів сторінки.

## Changes
`frontend/features/dashboard/components/AttentionTable.tsx` (єдиний змінений файл):
- Додано константу `VISIBLE_ROWS = 5`; рендеряться лише перші 5 рядків
  відфільтрованого списку (`filtered.slice(0, VISIBLE_ROWS)`).
- Під таблицею кнопка **«Переглянути всі (N)»** (N = повна кількість у поточному
  фільтрі), показується лише коли `filtered.length > 5`.
- Навігація: `/stock` для табу «All», `/stock?status=<filter>` для табів
  Expired / Critical / Warning — сторінка `/stock` вже читає `status` через
  `useSearchParams`, і значення статусів (`warning|critical|expired`) збігаються
  зі `StockFilters.STATUS_OPTIONS`, тож фільтр приходить преселектнутим.
- Стилі — inline dark-theme патерн дашборда (hover → #1D3461 / #3B82F6),
  як у філтр-табів того ж блоку.

## Target choice
Сторінки `/shelf` не існує (перевірено по `app/(dashboard)/**/page.tsx`).
`/stock` — сторінка партій зі статусами FEFO, підтримує `?status=` → обрано її.

## Verification
- `npx tsc --noEmit` — clean
- `npm run build` — green (37/37 pages)
