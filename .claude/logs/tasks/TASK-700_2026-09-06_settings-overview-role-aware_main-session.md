# TASK-700 — Рольова сторінка «Налаштування» + змістовна вкладка «Огляд»

**Status:** done · main session · запушено `origin/main` `6e353ef4` · Plan: `.claude/plans/curried-mapping-sparkle.md`

## Проблема
Вкладка «Загальні» на `/settings` була заглушкою (зміст сторінки + перемикач мови).
Сторінка не знала про ролі: пункт у сайдбарі не гейтився, вкладку «Інтеграції»
(ключі ПРРО / Claude API / Telegram bot) бачив кожен і впирався в 403; «Модулі»
гейтились `PROVIDER_TEAM`, тож адмін мережі не бачив модулі власного бізнесу.

## Зроблено (frontend-only)
- `general` → `overview`. Нова `features/settings/components/OverviewTab.tsx`:
  контекст-хедер (роль + компанія), read-only «Модулі бізнесу» (чіпи, з `useModules`),
  «Швидкі дії» (посилання на `/settings-user*` + `LanguageSwitcher`), «Команда та
  доступ» для `AT_LEAST_STORE_MANAGER` (лічильники активних / без магазину +
  кнопки «Керувати командою» / «Шаблони ролей» (адмін) / «Юридичні особи»).
- Гейтинг вкладок у `app/(dashboard)/settings/page.tsx`, дзеркалить бекенд:
  - Інтеграції → `canViewIntegrations(role, capabilities)` = `store_manager+` або
    capability `integrations.view` (новий хелпер у `lib/roles.ts`, мірорить
    `AppPolicies.IntegrationsViewOrCapability`);
  - Модулі → `PROVIDER_TEAM` **або** `ENTERPRISE_ADMIN_ONLY` (read-only);
  - прихована вкладка на прямий `?tab=` → fallback на `overview`.
- `AuthUserDto` (frontend) += `capabilities?: string[] | null` — бекенд уже віддавав.
- `/users` приймає `?tab=role-templates` (для кнопки «Шаблони ролей» з Огляду).
- i18n: `Dashboard.settings.generalTab` → `overviewTab` (uk + en, 19 ключів).
- `lib/roles.test.ts` += `canViewIntegrations` (6 кейсів).
- Пункт «Налаштування» в сайдбарі лишено видимим усім ролям (рішення користувача).

## Перевірка
- `tsc --noEmit` ✓ · `next lint` ✓ · `next build` ✓ (exit 0) · `vitest roles.test.ts` — 17/17 ✓
- У браузері (dev, тенант «Свіжий Кут»):
  - `ea@demo.local` (enterprise_admin): 4 вкладки, повний Огляд + блок «Команда» з
    кнопкою «Шаблони ролей», `/api/auth/me` віддає `capabilities: []`;
  - `keeper@demo.local` (storekeeper): 2 вкладки (Огляд + Сповіщення), Огляд без
    блоку «Команда», прямий `/settings?tab=integrations` → Огляд; мережеві запити 200.

## Примітки
- Комічено через тимчасовий git worktree — основна тека була зайнята гілкою
  `codex/mobile-app-menu-subgroups` (паралельна Codex-сесія). Бекенд не чіпався.
- Поза скоупом: блоки «Ідентифікація» / «Мій доступ», перенесення CRUD ролей у
  Налаштування (лишається на `/users`).
