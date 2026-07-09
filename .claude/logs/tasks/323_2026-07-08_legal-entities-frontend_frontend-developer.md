# TASK-323 — Legal Entities: frontend feature, forms wiring, nav

**Agent:** frontend-developer
**Date:** 2026-07-08
**Status:** done

## Summary

Frontend for Legal Entities (TASK-321/322 backend groundwork): new `legal-entities` feature,
a settings sub-page with CRUD, and wiring of a "Юридична особа" select into Location and User
forms.

## Files created

- `frontend/features/legal-entities/types.ts` — `LegalEntityDto`, `CreateLegalEntityDto`, `UpdateLegalEntityDto`.
- `frontend/features/legal-entities/api/legal-entities.ts` — CRUD wrapper (`api.get/post/put/delete`), mirrors `locations.ts`.
- `frontend/features/legal-entities/hooks/useLegalEntities.ts` — `useLegalEntities`, `useLegalEntity`, `useCreateLegalEntity`, `useUpdateLegalEntity`, `useDeactivateLegalEntity` (React Query, cache patched in `onSuccess`, same pattern as `useUsers.ts`).
- `frontend/features/legal-entities/components/LegalEntityFormDialog.tsx` — create/edit modal, react-hook-form + zod (ЄДРПОУ 8/10-digit regex, email format), styled like `LocationFormDialog.tsx` (inline styles — this codebase does not use shadcn/ui form primitives despite CLAUDE.md's aspirational convention; matched actual sibling code instead).
- `frontend/features/legal-entities/components/LegalEntitiesList.tsx` — table view; hides edit/deactivate actions when `canManage` is false.
- `frontend/app/(dashboard)/settings/legal-entities/page.tsx` — new route, list + dialog, gated by `canManageLegalEntities`.

## Files changed

- `frontend/lib/roles.ts` — added `AT_LEAST_ENTERPRISE_ADMIN` (provider + enterprise_admin, mirrors backend `AppPolicies.AtLeastEnterpriseAdminRoles`) and `canManageLegalEntities(role, permissions)` helper (role check OR `permissions["legal_entities.manage"] === true`, mirrors backend `LegalEntityAuthorization.CanManage`).
- `frontend/components/layout/Sidebar.tsx` — added "Юридичні особи" nav item (Landmark icon) to the "Персонал" group at `/settings/legal-entities`; visibility filtered via `canManageLegalEntities(userRole, me?.permissions)` in the `visibleItems` filter (role-only `Set` couldn't express the role-OR-permission-override logic, so it's special-cased by href).
- `frontend/features/locations/types.ts` — `LocationDto.legalEntityId: string | null` added.
- `frontend/features/locations/api/locations.ts` — `legalEntityId?: string | null` added to `CreateLocationDto`/`UpdateLocationDto`.
- `frontend/features/locations/components/LocationFormDialog.tsx` — added "Юридична особа" select (active entities only, via `useLegalEntities()`), wired into schema/reset/onValid/onSubmit payload.
- `frontend/app/(dashboard)/locations/page.tsx` — `handleSubmit` passes `legalEntityId` through to `create.mutate`.
- `frontend/features/users/types.ts` — `legalEntityId` added to `UserDto`, `InviteUserRequest`, `UpdateUserRequest`.
- `frontend/features/users/components/InviteUserModal.tsx` — added "Юридична особа" select (active entities only), wired into invite payload.
- `frontend/features/users/components/UserDetailPanel.tsx` — added "Юридична особа" select in the edit form, wired into `handleSave`'s `UpdateUserRequest`.

## API field casing confirmed

Backend DTOs (`LegalEntityDtos.cs`, `LocationDtos.cs`, `UserDtos.cs`) use PascalCase C# records;
ASP.NET Core's default camelCase JSON policy applies (confirmed — no custom `JsonNamingPolicy`
found in `ShelfGuard.Api`). Frontend types use camelCase matching: `legalName`, `edrpou`,
`legalAddress`, `directorName`, `phone`, `email`, `iban`, `bankName`, `isVatPayer`, `isActive`,
`createdAt`, `updatedAt`, `legalEntityId`.

## Verify

- `npx tsc --noEmit`: 0 errors.
- `npm run build`: succeeded, all 50+ routes generated including `/settings/legal-entities` (6.41 kB).

## Reviewer notes

- Permission-visibility: `canManageLegalEntities` is used in 3 places — the page's create-button/dialog gate, `LegalEntitiesList`'s per-row action buttons, and the Sidebar nav item. All three call the same helper so they can't drift out of sync with each other; the helper itself mirrors backend `LegalEntityAuthorization.CanManage` (enterprise_admin/provider role check OR `legal_entities.manage` truthy override) — double check `AuthUserDto.permissions` is actually populated for non-provider tenant roles when an override is granted (TASK-322 says `AuthService.ToDto` maps `u.LegalEntityId` and JWT gets a `permissions` claim, but confirm `/auth/me` response also serializes the same `permissions` dict the JWT claim was derived from).
- Route chosen: `/settings/legal-entities` (sibling of `/settings`, not nested under the tabbed `SettingsPage` component) — because Legal Entities needs its own list/table layout distinct from the tab-content pattern used by Notifications/Integrations/Modules tabs, and to mirror how `/locations` is a full standalone page rather than a Settings tab. No entry was added inside `SettingsPage`'s tab bar; only the Sidebar link points here directly.
- Did not touch `LocationFormDialog`'s "Store" variant — confirmed via grep that only one `LocationFormDialog.tsx` exists and it's the one routed at `/locations`.
