# TASK-374: Двомовність (укр/eng) лендінг сторінки через next-intl

**Agent:** frontend-developer
**Date:** 2026-07-16
**Status:** done

## Зроблено

- Встановлено `next-intl@4.13.2` (сумісний з Next.js 14.2.35).
- `frontend/i18n/routing.ts`, `frontend/i18n/request.ts`, `frontend/i18n/navigation.ts` — конфіг локалей (`uk` default без префіксу, `en` з `/en`), `localePrefix: as-needed`.
- `frontend/messages/uk.json`, `frontend/messages/en.json` — повний словник `Landing.*` (header, hero, problem, features, showcase, howItWorks, audience, pricing, faq, leadSection, leadForm, footer, meta). Англійський переклад — природний, не калька.
- `frontend/app/page.tsx` → `frontend/app/[locale]/page.tsx` + новий `frontend/app/[locale]/layout.tsx` (NextIntlClientProvider, generateStaticParams, setRequestLocale) + `locale-html-lang.tsx` (client-effect для `document.documentElement.lang`, корінний `app/layout.tsx` не чіпали).
- Усі 11 компонентів лендінгу перекладено: серверні — `getTranslations`, клієнтські (LandingHeader, LeadForm) — `useTranslations`. Масиви (FAQ, features, pricing included, nav, showcase blocks) перенесено в messages, іконки лишились у коді (мапляться по індексу).
- `LeadForm.tsx` — zod-схема будується функцією з `t`, усі валідаційні повідомлення, placeholder-и, success-стан і кнопка перекладені.
- Language switcher у `LandingHeader.tsx` (UA/EN toggle) на `next-intl` navigation `Link`, зберігає поточний hash/анкор (додав `hashchange` listener — без нього перемикач губив якір при кліку по in-page навігації).
- `frontend/middleware.ts` — `next-intl`'s `createMiddleware(routing)` викликається тільки для `/` і `/en*`, решта шляхів (dashboard/auth/API) йдуть через існуючу PROTECTED/AUTH_ROUTES логіку без змін.
- `frontend/next.config.js` — обгорнуто `createNextIntlPlugin`.

## Верифікація

- `npm run build` — успішно, SSG для `/uk` і `/en` (`● /[locale]` в виводі), 0 TS-помилок.
- `npm run lint` — 0 помилок.
- Dev-сервер + Browser pane: `/` з Accept-Language автовизначенням, `/uk` і `/en` явно рендерять весь текст перекладеним; UA/EN перемикач працює і зберігає якір; `/login` і редірект `/dashboard` → `/login` без сесії — не зламані; LeadForm валідація і success-стан перекладені на обох локалях; FAQ (6 пунктів) рендериться.

## Знайдені і виправлені проблеми
- Language switcher спочатку губив hash: `useEffect` читав `window.location.hash` лише один раз при mount. In-page якорні кліки (`#features` тощо) не ремонтять компонент, а лише генерують `hashchange` — додав listener на це подія.
- next-intl `Link` з `locale` prop і рядковим `href` (`"${pathname}${hash}"`) не зберігав hash-фрагмент — виправлено переходом на об'єктну форму `href={{ pathname, hash }}`.

## Не в скоупі (за планом)
`(dashboard)/*`, `(auth)/login` та решта 43 сторінок — лишились україномовними поза `[locale]` сегментом.
