# BUG-011 — банер «Час сеансу сплив» після ручного «Вийти»

**Agent:** frontend-developer · **Date:** 2026-07-03 · **Status:** done

## Проблема
Клік «Вийти» → на /login показується банер «Час сеансу сплив» (TASK-279).
Причина: після `authApi.logout()` (refresh cookie відкликано) in-flight polling
(SupportChatWidget кожні 3с, notifications badge) отримував 401 → `apiFetch`
пробував refresh (fail) → hard redirect `/login?reason=session_expired`,
що перебивав чистий `router.push("/login")` з `useLogout`.

## Зміни
- `frontend/lib/api.ts`:
  - module-level прапорець `_loggedOut` + export `markLoggedOut()`;
  - у 401-гілці: якщо прапорець — тихий `throw ApiError(401)` без refresh і без
    redirect (перевірка до і після `tryRefresh()` — покриває гонку, коли logout
    стається під час refresh);
  - якщо на момент запиту токена не було — редірект на `/login` без reason
    («не залогінений», а не «сеанс сплив»);
  - `setToken()` скидає прапорець (новий логін/успішний refresh).
- `frontend/features/auth/hooks/useAuth.ts`: `useLogout.mutationFn` викликає
  `markLoggedOut()` ПЕРЕД `authApi.logout()`.

## Регресія TASK-279
Не зачеплена: протухла сесія (токен був, refresh не вдався, прапорець не стоїть)
далі редіректить на `/login?reason=session_expired`.

## Verify
- `npx tsc --noEmit` — clean
- `npm run build` — green (40/40 pages)
