# TASK-376: Dashboard i18n foundation (uk/en) — Block 1

**Agent:** frontend-developer
**Date:** 2026-07-16
**Status:** done

## Зроблено

**Locale plumbing (нове):**
- `frontend/i18n/dashboard-locale.ts` — cookie `sg_locale` (1 рік, path=/), резолюція
  cookie → `user.preferredLocale` (кеш auth) → `navigator.language` (uk якщо починається з
  uk, інакше en; uk якщо нічого не визначилось) → uk. Event `sg-locale-changed` для
  live-оновлення без reload.
- `frontend/i18n/DashboardIntlProvider.tsx` — client-only `NextIntlClientProvider`,
  статичний import `messages/{uk,en}.json`, у provider йде тільки зріз `{Common, Dashboard}`
  (без `Landing`). SSR-дефолт locale="uk", реальна резолюція в `useEffect` (той самий
  патерн, що вже є `mounted`-гейт у dashboard layout).
- Підключено в `app/(dashboard)/layout.tsx` (розбито внутрішній `Loading`/логіку в
  `DashboardChrome`, обгорнуто провайдером) і `app/(auth)/layout.tsx`.
- `app/[locale]/layout.tsx` (лендінг) — messages звужено до `{ Landing: messages.Landing }`
  щоб нові namespace не потрапляли в лендінг-бандл (необхідний захисний one-liner через те,
  що namespace тепер у тому самому файлі).

**Переклад (~31 файл, namespaces `Common.*` + `Dashboard.*` у тих самих
`frontend/messages/{uk,en}.json`):**
- `components/layout/{Sidebar,SupportChatWidget,TopBar,UserMenu,StoreSelector}.tsx`
- `components/ui/{DateRangePicker,ReasonModal,TrendIndicator,ProductAnalyticsLink}.tsx` +
  `components/AccessDenied.tsx`
- `features/auth/components/{LoginForm,SessionExpiredNotice}.tsx` + новий
  `LoginCard.tsx` (винесено з `app/(auth)/login/page.tsx`, щоб та лишилась Server
  Component і зберегла `export const metadata`)
- `app/(dashboard)/layout.tsx` ("Завантаження…")

**Language switcher:** `features/profile/components/LanguageSwitcher.tsx` (новий) —
підключено в `features/settings/components/ProfileTab.tsx` (Settings → Profile, нова
секція 5). Пише cookie одразу (state-based re-render провайдера через подію, без
`router.refresh()`), паралельно викликає `useUpdateProfile()` (той самий `PUT /api/auth/me`,
розширений бекендом TASK-375 полем `preferredLocale`) — fire-and-forget, помилка нікому не
показується, перемикання вже відбулось через cookie.

**Backend-інтеграція:** `AuthUserDto.preferredLocale` + `UpdateProfileRequest.preferredLocale`
додано на фронті відповідно до контракту TASK-375 (перевірено по факту в git diff
бекенд-агента, не за здогадкою) — `PUT /api/auth/me { fullName, phone, preferredLocale }`
→ `UserDto` з `preferredLocale`.

## Верифікація

- `npm run build` — чисто, exit 0, всі 52 сторінки, повний type-check (ігнор
  `ignoreBuildErrors` не стоїть). Єдиний шум у логах — `next-intl` `ENVIRONMENT_FALLBACK`
  (немає `timeZone` у provider) — той самий pre-existing діагностичний код, що і в
  лендінгового Block 0 provider; не помилка, білд і рендер не ламає.
- `npm run lint` — чисто, 0 warnings/errors.
- Dev-сервер (порт auto-assigned, 3000 зайнятий Docker) + браузер:
  - `/login` без cookie → рендер за `navigator.language` браузера (en); з
    `sg_locale=uk` cookie → миттєво українською. Підтверджує весь ланцюжок резолюції.
  - `/` → uk (default, unprefixed), `/en` → en — лендінг не зачеплений (перевірено і без
    cookie через detection, і explicit через `NEXT_LOCALE` cookie).
  - `/dashboard` без сесії → редірект на `/login` (middleware.ts не чіпали, поведінка та сама).
  - Консоль: 0 помилок, 0 hydration-warnings.
- Скрипт-звірка (одноразовий, scratchpad): витягнув усі 275 викликів `t("…")`/`tXxx("…")`
  з усіх 15 перекладених файлів і звірив кожен ключ проти обох `messages/{uk,en}.json` —
  усі 275 резолвляться в обох локалях. Закриває ризик namespace/key-тайпо в
  Sidebar/TopBar/UserMenu/etc, які не пройшли живий логін (нема тестових кредс — бекенд
  паралельно ще мігрує, не піднімав повний docker-стек заради цього).

## Файли

Плюс до перелічених у скоупі: `frontend/i18n/dashboard-locale.ts`,
`frontend/i18n/DashboardIntlProvider.tsx`, `frontend/features/auth/components/LoginCard.tsx`,
`frontend/features/profile/components/LanguageSwitcher.tsx`,
`frontend/features/auth/types.ts` (+`preferredLocale`), `frontend/features/profile/types.ts`
(+`preferredLocale`), `frontend/features/profile/hooks/useProfile.ts` (кеш-патч),
`.claude/launch.json` (додав `autoPort: true` для frontend-dev — порт 3000 зайнятий Docker).

## Не в скоупі (свідомо)
- Решта feature-модулів (inventory, pos, ...) — наступні блоки плану.
- `<title>` на `/login` лишився статично українською (server component, cookie/browser
  локаль там недоступні без `cookies()`-рефактору; сторінка ж сама рендериться потрібною
  мовою) — низький пріоритет, тільки текст вкладки браузера.
- Справжній bundle-split (окремі файли на namespace) не робив — план явно казав класти
  `Dashboard`/`Common` у ті самі `messages/{uk,en}.json`, що і `Landing`; замість цього
  на клієнт іде тільки зріз `{Common, Dashboard}` (без Landing) через явний вибір ключів
  у `DashboardIntlProvider`.
