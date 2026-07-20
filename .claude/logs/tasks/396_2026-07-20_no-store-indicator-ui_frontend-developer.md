# TASK-396: "Магазин не призначено" — індикатор + фільтр на списку користувачів

**Agent:** frontend-developer
**Date:** 2026-07-20
**Status:** done

## Зроблено

1. **`frontend/features/users/types.ts`** — `UserDto.needsLocationAssignment: boolean`
   (обов'язкове поле, 1:1 дзеркало бекенд-контракту TASK-395).
2. **`frontend/features/users/components/UsersList.tsx`**:
   - `NeedsLocationBadge` — попереджувальний pill (амбер `#FBBF24`/`#78350F`/`#2D2208`, той
     самий палет що вже є в цьому файлі й в `InviteUserModal`'s `partialError`; свідомо не
     червоний) у колонці "Роль", поруч з `RoleBadge`/`TenantRoleBadge`; рендериться лише коли
     `user.needsLocationAssignment === true`.
   - Чекбокс "Показати тільки без магазину" у filter bar — локальний `useState`, доданий у
     той самий `filtered`-предикат що search/role/status (без нового API-запиту, чисто
     client-side над уже завантаженим `useUsers()` списком).
   - Виправив умову порожнього стану (`notFound` vs `noUsersYet`) — додав новий фільтр до
     умови, інакше toggle з 0 результатів і всіма іншими фільтрами на "all" помилково показував
     би "Користувачів ще немає" замість "Нічого не знайдено".
3. **`app/(dashboard)/users/page.tsx`** — 4-та стат-плитка "Без магазину" (той самий амбер)
   поруч із Total/Active/Telegram у стат-рядку; рахується client-side з уже завантаженого
   `useUsers()` списку, без нового ендпоінта.
4. **i18n** — нові ключі в обох `frontend/messages/{uk,en}.json`: `list.needsLocationBadge`,
   `list.onlyMissingLocationLabel`, `page.statNeedsLocation`. Редагував виключно через
   Edit-тул (не PowerShell Get-Content/Set-Content — зламало б кирилицю, див. пам'ять); JSON-
   валідність і коректність кирилиці перевірив через `node -e "require(...)"`.

## Верифікація

- `npx tsc --noEmit` — 0 помилок.
- `npm run lint` — 0 попереджень/помилок.
- `npm run build` — успішно, 52/52 сторінок згенеровано, `/users` у маніфесті (15.3 kB).
  Повторювані `ENVIRONMENT_FALLBACK` рядки під час "Generating static pages" — той самий
  шум, що TASK-392c вже задокументував як pre-existing/не пов'язаний зі змінами.
- `docker build -f frontend/Dockerfile frontend` (з кореня репо) — **exit 0**. Docker Desktop
  на старті задачі був вимкнений — довелось запустити вручну і почекати холодний старт
  WSL2-бекенду (~6 хв), інакше без сюрпризів.
- **Додатковий живий e2e-чек у браузері** (понад обов'язковий список — Docker/Postgres все
  одно піднімались для build-кроку, а backend TASK-395 вже живе на local `main`): підняв
  `docker compose up -d postgres` + `backend-dev`/`frontend-dev` (з `.claude/launch.json`),
  залогінився як seed `ea@demo.local` (enterprise_admin). На існуючих dev-даних (9
  користувачів) усе збіглось точно: 8 з 9 отримали бейдж "No store assigned" (усі, крім
  enterprise_admin — network_manager/store_manager/merchandiser/storekeeper/staff, включно з
  трьома тестовими staff-акаунтами з TASK-349), стат-плитка показала "8 Without a store",
  чекбокс-фільтр коректно звузив список до 8 рядків і footer до "8 of 9 users" — і жодного
  нового `GET /api/users` в Network при тогглі (підтверджено: чисто client-side). Зупинив обидва
  preview-сервери після перевірки; Postgres-контейнер лишив увімкненим (штатний
  `docker compose up -d` dev-стан, не одноразовий).

## Не в скоупі (свідомо, як і просив бриф)

- RLS/enforcement — не чіпав.
- Сам процес призначення магазину (UserDetailPanel/InviteUserModal, TASK-392c) — тільки
  видимість, як і просив бриф.
- Git: локальний commit, без push (продукт-овнер перевіряє й пушить сам).
