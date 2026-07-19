# TASK-392c: Store-scoped user assignment — frontend (Feature 2 Stage 1)

**Agent:** frontend-developer
**Date:** 2026-07-19
**Status:** done

## Передумова: 392b не було в worktree на старті

Бриф стверджував, що TASK-392b (API-шар) уже присутній у цьому worktree. Насправді
worktree гілка (`worktree-agent-aa649a8524e6a5e73`) була створена від local `main` ДО того,
як 392b туди докомітився — worktree HEAD (`516a4178`, лише схема TASK-392) виявився чистим
предком поточного local `main` (`b342da61`, включно з 392b і паралельним 391b). Зробив
`git merge main --ff-only` (чистий fast-forward, без конфліктів, нічого не чіпає з моїх
файлів) — після цього `UsersController.UpdateLocations`/`GetLocations`,
`IUserService.SetLocationsAsync`/`GetLocationsAsync`, `UpdateUserLocationsRequest`/
`UserLocationsDto` стали доступні для звірки контракту.

## Зроблено

1. **`frontend/features/users/types.ts`** — додав `staff: 0` у `ROLE_RANK` (бекенд це вже
   мав, фронтенд-мапа — ні; коментар "mirrors backend" був фактично застарілим). Додав
   `SINGLE_LOCATION_ROLES`/`isSingleLocationRole()` (1:1 дзеркало backend
   `UserService.SingleLocationRoles`) і `UserLocationsDto`/`UpdateUserLocationsRequest`
   (`{ locationIds: string[] }`).
2. **`frontend/features/users/api/users.ts`** — `getLocations(id)` /
   `setLocations(id, data)` → `GET`/`PUT /api/users/:id/locations`.
3. **`frontend/features/users/hooks/useUserLocations.ts`** (новий) — `useUserLocations(id)`
   (GET, React Query) + `useSetUserLocations()` (PUT, unbound id — той самий патерн, що й
   `useAssignTenantRole()`, бо в InviteUserModal id відомий лише ПІСЛЯ invite).
4. **`frontend/features/users/components/InviteUserModal.tsx`**:
   - `INVITE_ROLES` (хардкод) → `invitableRoles` = `ROLE_KEYS` (без `"provider"`),
     відфільтровані `(ROLE_RANK[r] ?? 0) <= myRank`. Дефолт — `"store_manager"`, якщо
     доступний (як і раніше), інакше перший доступний.
   - store_manager-і-нижче: мовчазний auto-assign `me.storeId`; якщо в інвайтера
     самого немає storeId — одиночний dropdown-picker (fallback).
   - `network_manager`: чекбокс-мультиселект територій (`useLocations()`), застосовується
     через `useSetUserLocations()` ПІСЛЯ успішного invite (двокроковий flow, той самий
     "created-but-partial-failure" патерн, що вже був для TenantRole).
   - `enterprise_admin`: жодного store-related UI, `storeId: null`.
5. **`frontend/features/users/components/UserDetailPanel.tsx`** — одиночний store-picker
   в edit-формі (лише коли `isSingleLocationRole(поточна обрана роль)`), звичайний Update
   endpoint. Новий `canManageLocations` (`AT_LEAST_ENTERPRISE_ADMIN && !isSelf &&
   user.role === "network_manager"`) додано в `showAccessTab`.
6. **`frontend/features/users/components/UserLocationsEditor.tsx`** (новий) — мульти-select
   територій для `network_manager`-таргета, full-replace (GET на mount, PUT на Save,
   dirty-check), стиль/структура — точна копія патерну `TenantRoleSelector.tsx`.
7. **i18n** — нові ключі в `frontend/messages/{uk,en}.json`: `users.detailPanel.storeLabel`/
   `storeNoneOption`; `users.inviteModal.storeLabel/storeNoneOption/territoryLabel/
   territoryHint/territoryEmptyHint/assigningLocationsButton/partialErrorMessageLocations`;
   нова секція `users.locationsEditor.*`. Обидві мови синхронно, JSON-валідність перевірено.

`features/tenant-roles/*` і `Sidebar.tsx` не чіпав (паралельна Feature 1).

## Верифікація

- `npx tsc --noEmit` — 0 помилок.
- `npm run lint` — 0 попереджень/помилок.
- `npm run build` — успішно (52/52 сторінок, `/users` route в маніфесті). Побачив
  повторювані `ENVIRONMENT_FALLBACK` помилки під час "Generating static pages" — перевірив
  через `git stash -u` + rebuild на ДО-змінному стані: та сама кількість (38), той самий
  фінальний exit 0 → підтверджено pre-existing, не пов'язано з цією задачею.
- `docker build -f frontend/Dockerfile frontend` (з кореня worktree) — exit 0, image
  зібрано й видалено після перевірки.
- Git: локальний commit у цьому worktree, **без push** (пауза на деплой).

## Не в скоупі (свідомо)

- `EDITABLE_ROLES` у `UserDetailPanel.tsx` (роль-дропдаун при редагуванні) досі не містить
  `"staff"` — pre-existing gap, бриф просив лише додати store/territory UI, не чіпав
  role-list там. Вартий окремої задачі, якщо продукт хоче дозволити зміну ролі на staff
  через цю форму.
- Stage 3 RESTRICTIVE RLS (`product_stock`/`daily_sales`/`pos_shifts`/etc.) — не тут.
- `.claude/docs/api-contracts.md` — Users-секція вже застаріла до цієї задачі (за нотатками
  392b), holistic reconciliation краще окремою documentation-writer задачею.
