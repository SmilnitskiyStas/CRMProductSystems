# TASK-675 — Каталог: керування штрихкодами (Частина A плану)

**Стан:** done · **Агент:** main-session · **Дата:** 2026-09-02

Частина A з плану `.claude/plans/1-giggly-catmull.md` (Каталог — штрихкоди та глобальні категорії). Частини B (глобальні категорії) — окремі задачі.

## Що зроблено

**Модель даних — без змін.** `Item.Barcodes: List<string>` (jsonb). Конвенція `Barcodes[0]` = основний/актуальний ШК (вже фактична поведінка POS/receipts/analytics/mobile lookup). «Зробити основним» = перемістити на індекс 0.

**Backend:**
- `ItemService.cs` — новий `NormalizeBarcodes()`: trim, відкидання порожніх, дедуп зі збереженням порядку. Застосовано в `CreateAsync` і `UpdateAsync` (було `request.Barcodes ?? []` сирим).

**Frontend:**
- `features/inventory/components/BarcodeCell.tsx` — **новий**. У колонці таблиці: завжди primary (`barcodes[0]`); при >1 ШК — пілюля «+N»; hover/click → портальний поповер (патерн `ActionMenu.tsx`: createPortal + getBoundingClientRect + outside-click/scroll close) зі списком усіх ШК, primary позначено ★ + «Основний».
- `ProductsTable.tsx` — колонка `barcode` рендерить `<BarcodeCell>`; drawer `ProductDetail` показує весь список (★ на першому).
- `ProductForm.tsx` — на кожному чипі ШК: primary (перший) = зелений з ★; решта = кнопка «☆ Зробити основним» (`makePrimary` → `[b, ...prev.filter(x=>x!==b)]`). Хінт-текст при >1 ШК.
- `app/(dashboard)/inventory/[id]/page.tsx` — поле «Штрихкод» показує весь список.
- `messages/uk.json` + `en.json` — ключі `form.barcodeMakePrimary`, `form.barcodePrimaryHint`, `table.barcodeMore`, `table.barcodeAllTitle`, `table.barcodePrimary`.

## Верифікація

- `npx tsc --noEmit` (frontend) — чисто.
- `dotnet build ShelfGuard.Application` — чисто (0 warn / 0 err).
- E2E у браузері (dev-стек: docker compose + `dotnet run` :5000 + `next dev` :3001, демо-дані «Свіжий Кут», логін `manager@demo.local`):
  - Створено товар «ZZ Штрихкод Тест» з 3 ШК; «Зробити основним» на 3-му → перемістився на початок, зберігся як `Barcodes[0]`.
  - Таблиця: комірка показує `3333333333333` + пілюлю «+2».
  - Поповер (click/hover): «ALL BARCODES | ★ 3333333333333 Primary | 1111111111111 | 2222222222222».
  - Console — без помилок.

## Нотатки

- MCP-браузер: синтетичний `hover` не завжди тригерить React `onMouseEnter` — у реальному браузері hover працює; верифіковано через click + DOM-інспекцію.
- Не деплоєно. Тестовий товар «ZZ Штрихкод Тест» лишився в дев-БД (прибрати при фінальному прибиранні).
