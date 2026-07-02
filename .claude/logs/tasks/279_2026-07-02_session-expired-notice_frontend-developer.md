# TASK-279 — Повідомлення про завершення сеансу при неактивності

**Agent:** frontend-developer
**Date:** 2026-07-02
**Status:** done

## Problem

Коли access token протухає і refresh не вдається, `frontend/lib/api.ts` робив
`clearToken()` + жорсткий `window.location.href = "/login"` без жодного пояснення.
Користувач опинявся на сторінці логіну і не розумів чому.

## Changes

### 1. `frontend/lib/api.ts`
- Redirect після невдалого refresh: `/login` → `/login?reason=session_expired`.
- Жорстка навігація (`window.location.href`) збережена свідомо — вона скидає весь
  in-memory state (React Query cache, Zustand). Додано коментар із поясненням.

### 2. `frontend/features/auth/components/SessionExpiredNotice.tsx` (new)
- `"use client"` компонент; читає `reason` через `useSearchParams`.
- Якщо `reason=session_expired` — рендерить банер `role="status"`:
  «Час сеансу сплив. Будь ласка, увійдіть знову.»
- Стилістика повторює патерн error-блока в `LoginForm` (bg 1A / border 40 / текст),
  але у warning-тоні amber `#F59E0B` замість червоного — це очікувана подія, не помилка.
- Параметр в URL не чиститься (допустимо за умовами задачі; middleware при наявності
  сесії все одно редіректить з /login на /dashboard, тож stale-URL не проблема).

### 3. `frontend/app/(auth)/login/page.tsx`
- Сторінка лишається server component (зберігає `metadata`).
- `SessionExpiredNotice` вставлено над `<LoginForm />`, обгорнуто в
  `<Suspense fallback={null}>` — обов'язково для `useSearchParams` у статично
  пререндереній сторінці (інакше `npm run build` падає з
  "useSearchParams() should be wrapped in a suspense boundary").

### 4. `frontend/middleware.ts` — БЕЗ ЗМІН (рішення)
Middleware редіректить на /login лише коли немає ні `sg_session`, ні `refreshToken`
cookie. У цьому стані неможливо відрізнити «сеанс був і сплив» від «користувач
вперше відкрив сайт» — cookie відсутні в обох випадках. Показувати «час сеансу
сплив» новим відвідувачам не можна, тому reason ставить тільки api.ts, який знає,
що refresh реально виконувався і провалився. Кейс «sg_session є, refreshToken
протух» middleware пропускає далі — його ловить client-side 401→refresh→fail
у api.ts, який і додає параметр.

## Verification

- `npx tsc --noEmit` — green (no output).
- `npm run build` — green; `/login` static, 37/37 pages generated.

## Files changed

- `frontend/lib/api.ts` (modified)
- `frontend/app/(auth)/login/page.tsx` (modified)
- `frontend/features/auth/components/SessionExpiredNotice.tsx` (new)
- `.claude/tasks/current.md` (TASK-279 entry)
