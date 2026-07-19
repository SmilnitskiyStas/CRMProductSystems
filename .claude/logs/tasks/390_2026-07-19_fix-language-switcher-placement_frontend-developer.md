# TASK-390: Language switcher placement fix (i18n Block 1 bugfix)

**Agent:** frontend-developer
**Date:** 2026-07-19
**Status:** done

## Проблема

`ProfileTab.tsx` (`frontend/features/settings/components/ProfileTab.tsx`), куди Block 1
(TASK-376) вписав `<LanguageSwitcher />`, ніде не імпортувався — мертвий код, ніколи не
рендерився в проді. Реальна сторінка `/settings-user` рендерить форми напряму, минаючи цей
файл. Користувач попросив перемикач мови на `/settings` (тенант-скоуп), вкладка "Загальна".

## Зроблено

1. `frontend/app/(dashboard)/settings/page.tsx` — імпорт
   `LanguageSwitcher` з `@/features/profile/components/LanguageSwitcher`, підключено в
   `GeneralTab()` як окрема картка (стиль узгоджений з існуючою "User settings hint"
   карткою: `#0A1020`/`#1F2937`/borderRadius 9), розміщена після списку
   notifications/integrations rows, перед карткою-хінтом на `/settings-user`.
   `LanguageSwitcher.tsx` сам не чіпав (самодостатній, вже мав власні title/subtitle —
   дублювати заголовок не було сенсу).
2. Видалив `frontend/features/settings/components/ProfileTab.tsx` повністю. Перевірено
   grep-ом до і після: єдиний імпортер — сам файл (його ж `export function ProfileTab()`);
   `MarketplaceProfileTab` — окремий, активно використовуваний компонент, не займав.
3. Прибрав орфанований переклад-неймспейс `Dashboard.settings.profileTab.*` (8 ключів) з
   `frontend/messages/{en,uk}.json` — використовувався виключно видаленим файлом.
   `Dashboard.profile.language` (сам LanguageSwitcher) і `Dashboard.settings.userSettingsPage`
   (окрема, активна сторінка `/settings-user`) — не займав.
4. `settings-user/page.tsx` не чіпав — підтверджено, що він і так не імпортував `ProfileTab`.

## Верифікація

- `node -e "JSON.parse(...)"` на обох messages-файлах — валідний JSON після видалення блоку.
- `npm run lint` — exit 0, "No ESLint warnings or errors".
- `npx tsc --noEmit` — exit 0, без помилок.
- `npm run build` — exit 0, усі 52 сторінки згенеровано, `/settings` — 14.3 kB.
  (`ENVIRONMENT_FALLBACK`/timeZone-попередження в логах — пре-існуючий шум next-intl,
  трапляється і на `/login`, якого це завдання не торкалось; не "Failed to compile".)
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — виконано СИНХРОННО,
  `EXIT: 0`. Усі стадії, включно з `npm run build` усередині контейнера й
  `exporting to image`, без помилок.
- Dev-сервер (`next dev`) + браузер: `/settings` під non-auth сесією рендерить лише
  client-side loading shell і редіректить на `/login` (`DashboardChrome` в
  `app/(dashboard)/layout.tsx` — гейт на `getToken()`/`useMe().error`) — очікувана
  поведінка, не регресія. Логін-креденшли вводити не можна (safety policy), тож
  повний інтерактивний клік-тест "вкладка Загальна → перемикач" не виконував.
  Замість цього перевірив скомпільований client bundle `.next/static/chunks/app/
  (dashboard)/settings/page.js` — містить повний код `LanguageSwitcher` (`sg_locale`,
  `resolveDashboardLocale`, `setDashboardLocale`, `sg-locale-changed`), тобто компонент
  реально забандлений у `/settings`-чанк, а не dead-code-eliminated.

## Не в скоупі (свідомо)

- `LanguageSwitcher.tsx` — не переписував, тільки підключив.
- `settings-user/page.tsx` — не чіпав.
- Git commit/push — за інструкцією, користувач сам комітить і стежить за CI/деплоєм.
