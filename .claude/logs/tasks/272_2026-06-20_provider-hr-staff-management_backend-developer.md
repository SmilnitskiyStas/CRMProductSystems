# TASK-272 — Provider HR: управління власним персоналом

**Date:** 2026-06-20
**Agent:** backend-developer + frontend-developer
**Status:** done

## Scope

Розширення управління командою провайдера: редагування учасника (ім'я + роль) + реактивація деактивованих членів.

## What was already done (before this task)

- `GET /api/provider/team` — список команди
- `POST /api/provider/team/invite` — запросити учасника
- `DELETE /api/provider/team/{id}` — деактивувати учасника
- Frontend: `TeamTab.tsx`, `InviteProviderMemberModal.tsx`, `useProviderTeam.ts`, `providerTeamApi.ts`

## Changes

### Backend

**`ProviderTeamDtos.cs`** — додано `UpdateProviderMemberRequest(FullName, Role)`

**`IProviderTeamService.cs`** — додано два методи:
- `UpdateMemberAsync(Guid, UpdateProviderMemberRequest, CancellationToken)`
- `ReactivateMemberAsync(Guid, CancellationToken)`

**`ProviderTeamService.cs`** — реалізовано:
- `UpdateMemberAsync`: валідація fullName + role → guard на owner (не можна змінити роль `provider`) → `UpdateProfile` + `SetRole` → save
- `ReactivateMemberAsync`: знаходить user, перевіряє ProviderTeamRoles, `Activate()` → save

**`ProviderTeamController.cs`** — додано:
- `PUT /api/provider/team/{memberId}` — [ProviderCanInvite] → `UpdateMemberAsync`
- `POST /api/provider/team/{memberId}/reactivate` — [ProviderCanInvite] → `ReactivateMemberAsync`

### Frontend

**`providerTeamApi.ts`** — додано `UpdateProviderMemberRequest`, `updateMember()`, `reactivateMember()`

**`useProviderTeam.ts`** — додано `useUpdateMember()`, `useReactivateMember()`

**`EditMemberModal.tsx`** (НОВИЙ) — форма редагування: ім'я + роль; роль owner (`provider`) задизейблена з поясненням

**`TeamTab.tsx`** (ОНОВЛЕНИЙ):
- Лічильник учасників у заголовку
- Кнопка редагувати (Pencil icon) → `EditMemberModal`
- Кнопка «Відновити» (RefreshCw icon) для неактивних членів

## Business rules enforced

- Не можна змінити роль власника (`provider`) — захист від lockout
- `ReactivateMemberAsync` перевіряє ProviderTeamRoles (не можна реактивувати tenant users)
- `ProviderCanInvite` policy на PUT і reactivate (provider + provider_admin)

## Verification

- `dotnet build` — green (0 warnings, 0 errors)
- `tsc --noEmit` — green

## Next

- TASK-273: Provider employee performance statistics
- TASK-274: Provider schedule/calendar UI for provider_agent
