# TASK-745 — Ковзна сесія: фікс вибивання через 5–10 хв + idle logout 5 хв

**Першопричина:** access JWT живе 15 хв (`Jwt:AccessTokenMinutes`), refresh-токен ротується (7 днів). Коли access спливає, паралельні запити (polling, віджети) всі отримують 401 і кожен окремо викликає `/api/auth/refresh` з одним cookie. Перший ротує токен, інші подають уже відкликаний → детектор reuse (TASK-329) відкликає ВСІ сесії користувача → редірект на `/login`. Те саме між вкладками. Idle-логіки (TASK-712, гілка `feat/idle-session-logout`) у main не було.

**Зміни:**
- `frontend/lib/api.ts`: single-flight refresh + `navigator.locks` між вкладками (вкладка, що чекала, бере токен, оновлений іншою); одна повторна спроба після 401 від refresh; мережева/5xx помилка refresh більше не розлогінює.
- Backend `AuthService.RefreshAsync`: grace-вікно 10 с — повторне подання щойно ротованого токена НЕ відкликає родину (реальний replay пізніше — як і раніше відкликає). Терміни життя токенів не змінені.
- Idle logout (cherry-pick TASK-712 + доробки): 5 хв (`NEXT_PUBLIC_IDLE_TIMEOUT_MS` для QA), події mousemove/mousedown/click/keydown/wheel/scroll/touchstart, sync між вкладками (localStorage), перевірка на visibilitychange, вихід в одній вкладці виводить інші. Фонові запити активністю не є.
- Тести: AuthServiceTests (grace + backdated reuse), api.test.ts (single-flight, 5xx), useIdleLogout.test.ts.

**Статус:** `dotnet test --filter Auth` 417/417, vitest, tsc, lint — ок. У браузері не перевірялось (немає облікових даних). Mobile не чіпали (контракт `/refresh` незмінний).
