# TASK-023 — Users API (HR module)
**Agent:** backend-developer
**Date:** 2026-06-10
**Status:** done

## Summary
TASK-023 was already fully implemented in a prior session. Verified all layers complete. `dotnet build` passes with 0 errors.

## Implemented files
| Layer | File |
|---|---|
| Application | `ShelfGuard.Application/Features/Users/IUserService.cs` |
| Application | `ShelfGuard.Application/Features/Users/UserService.cs` |
| Application | `ShelfGuard.Application/Features/Users/Dtos/UserDtos.cs` |
| Api | `ShelfGuard.Api/Controllers/UsersController.cs` |

## Endpoints
```
GET    /api/users                        [AtLeastStoreManager]    → UserDto[]
GET    /api/users/{id}                   [AtLeastStoreManager]    → UserDto | 404
POST   /api/users/invite                 [AtLeastEnterpriseAdmin] → 201 UserDto | 400
PUT    /api/users/{id}                   [AtLeastStoreManager]    → UserDto | 400 | 404
PUT    /api/users/{id}/permissions       [AtLeastStoreManager]    → UserDto | 400 | 403 | 404
DELETE /api/users/{id}                   [AtLeastEnterpriseAdmin] → 204 | 404
GET    /api/users/{id}/activity          [AtLeastStoreManager]    → ActivityLogDto[] | 404
```

## Key business rules
- Role hierarchy check: editor must outrank target to edit permissions
- Invite: email uniqueness enforced, password min 8 chars
- Deactivate: soft delete (`IsActive = false`), not hard delete
- Activity log written on: invite, update, deactivate, password change, telegram link, permissions update
- Permissions: `PUT /permissions` with empty `{}` clears all overrides → role defaults

## Build result
`dotnet build` — 0 Warnings, 0 Errors ✅
