# TASK-334 — Public marketing landing page (/)

**Agent:** frontend-developer · **Date:** 2026-07-10 · **Status:** done

## Що зроблено
- `frontend/app/page.tsx`: redirect → server-component лендінг (SSG, SEO metadata + OpenGraph, og:image=/landing/dashboard-1.jpg, metadataBase=agrusystems.pp.ua).
- Нова фіча `frontend/features/landing/`: `landing.css` (scoped `[data-landing]`, reveal-анімації через `@media (scripting: enabled)` — no-JS/краулери бачать усе), `api/leads.ts` (plain fetch, без auth), компоненти: Logo (SVG щит+полиці, wordmark «ShelfGuard by AgruSystems»), LandingHeader (sticky, mobile-меню), Hero, Problem, Features (8 карток), Showcase (3 блоки текст/зображення), HowItWorks, Audience, Pricing («за запитом»), FAQ (native details/summary), LeadSection+LeadForm (RHF+zod, honeypot `website`, 204/400/429 за контрактом TASK-333), Footer, BrowserFrame, Reveal (IntersectionObserver).
- Скриншоти `img/*.jpg` → `frontend/public/landing/` (6 шт., next/image + sizes, hero priority).
- `app/layout.tsx`: `lang="en"` → `lang="uk"`. Додано `app/icon.svg` (favicon, раніше не було).

## Дизайн
Темна тема fixed (#0B0F17), стиль Linear/Vercel: бордери white/8%, картки white/3%, синій CTA #2D7DD2 (як у застосунку), статусне тріо green/amber/red для сторітелінгу. Скриншоти в browser-chrome рамці, hero — з м'яким синім glow.

## Верифікація
- `npx tsc --noEmit` clean; `npm run build` success, `/` prerendered static (title/lang/og перевірені в HTML).
- Браузер: desktop і mobile лейаути, без горизонтального скролу, консоль чиста, lazy-load нижче фолда, якорі працюють, FAQ розкривається, форма: клієнтська валідація ✓, POST на `/api/public/leads` ✓ (локально без бекенда — graceful помилка з'єднання).

## Не зроблено / нотатки
- Backend не чіпав (TASK-333 — паралельно, контракт узгоджено).
- Смоук лендінг+бекенд разом — на проді після деплою обох задач.
