# TASK-331 — Frontend: 2FA UI + auth hardening UX

**Agent:** frontend-developer · **Date:** 2026-07-09 · **Status:** done
**Contract:** `.claude/logs/handoffs/330-backend-to-frontend.md`

## Зроблено

**Login 2FA step**
- `features/auth/types.ts`: `LoginResponse` → union (`LoginSuccessResponse` | `TwoFactorChallengeResponse`) + guard `isTwoFactorChallenge`; `AuthUserDto.twoFactorEnabled`; типи 2fa verify/setup/enable/disable.
- `features/auth/api/auth.ts`: `login` не зберігає токени при challenge; додано `verifyTwoFactor`, `setupTwoFactor`, `enableTwoFactor`, `disableTwoFactor`.
- `features/auth/hooks/useAuth.ts`: спільний `useCompleteLogin` (clear cache → redirect за роллю), `useLogin` пропускає challenge, новий `useVerifyTwoFactor`.
- `features/auth/components/LoginForm.tsx`: крок 2 — 6-значний код (autofocus, numeric, `one-time-code`), тогл «Використати код відновлення» (XXXX-XXXX), «← Назад». Помилки UA: 401 → «Невірний код» / (challenge expired) «Час підтвердження вичерпано…», 429 → «Забагато спроб. Спробуйте пізніше.» (на обох кроках).
- `lib/api.ts`: **необхідний фікс** — 401 з `/api/auth/2fa/verify` (невірний код) раніше запускав refresh→hard-redirect на /login і знищував challenge-стан; тепер обидва анонімні auth-шляхи виключені з retry-логіки.

**Profile security section**
- Новий `features/profile/components/TwoFactorSection.tsx`: статус з `useMe().twoFactorEnabled`; enable-флоу setup → QR (`qrcode.react`, new dep) + copyable secret → код → recovery-коди (показ один раз, copy-all, warning, checkbox + кнопка «Я зберіг коди» обовʼязкові для закриття); disable-флоу — Modal з паролем + кодом (TOTP або recovery). `invalidateQueries(ME_KEY)` після enable/disable.
- Хуки `useTwoFactorSetup/Enable/Disable` у `features/profile/hooks/useProfile.ts`.
- Підключено в `app/(dashboard)/settings-user/page.tsx` (секція «Двофакторна автентифікація») та `features/settings/components/ProfileTab.tsx`.

**ChangePasswordForm**
- Клієнтська валідація 12+ символів, літери+цифри; hint «Мінімум 12 символів, літери та цифри»; серверні `{error}` показуються as-is; success → sonner toast «Пароль оновлено / Інші пристрої буде розлогінено.»; прибрано фейковий текст «API не реалізовано».

## Статус
- `npx tsc --noEmit` — clean. `npm run build` — success (50/50 сторінок).
- `npm run lint`: у frontend/ немає ESLint-конфіга (pre-existing — `next lint` завжди питає інтерактивно). Змінені файли перевірено напряму `eslint` з `next/core-web-vitals` — clean (виправлено 2 pre-existing `react/no-unescaped-entities` у ProfileTab.tsx).
- Live-перевірка з бекендом не виконувалась (потрібен користувач з увімкненою 2FA).

## Не в scope / залишилось
- Password-policy hint у формах invite user / tenant onboarding / provider team invite (бекенд валідує, UA-повідомлення бекенда показуються через існуючий error-контракт).
- Mobile login (Expo) не підтримує 2FA-челендж — окрема задача для mobile-developer.
