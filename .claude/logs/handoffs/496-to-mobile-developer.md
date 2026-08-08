# Handoff: TASK-496 (backend-developer) → mobile-developer

**Date:** 2026-08-08
**From:** backend-developer (TASK-496)
**To:** mobile-developer (parallel task, same working tree, consuming this contract)

## What changed

`POST /api/mobile-auth/login` and `POST /api/mobile-auth/register` no longer return a single
`accessToken`. They now return two nullable token fields. Implemented exactly per the brief I was
given — no deviations. Full source of truth:
`backend/ShelfGuard.Application/Features/MobileAuth/Dtos/MobileAuthDtos.cs` (`MobileLoginResponse`)
and `backend/ShelfGuard.Api/Controllers/MobileAuthController.cs`.

## Exact wire shape, all branches (camelCase, default ASP.NET Core policy — no `AddJsonOptions`)

**1. Consumer only, no linked active staff account:**
```json
{
  "personalAccessToken": "consumer-jwt",
  "workspaceAccessToken": null,
  "user": { "id": "...", "fullName": "...", "email": null, "phone": "...", "tenantId": null, "storeId": null },
  "access": { "canAccessWorkspace": false, "role": "consumer", "permissions": {}, "capabilities": [], "tabs": [] }
}
```

**2. Consumer linked to an active staff `User`, no 2FA:**
```json
{
  "personalAccessToken": "consumer-jwt",
  "workspaceAccessToken": "staff-jwt",
  "user": { "id": "...staff user id...", "fullName": "...", "email": "...", "phone": null, "tenantId": "...", "storeId": "..." },
  "access": { "canAccessWorkspace": true, "role": "...", "permissions": {...}, "capabilities": [...], "tabs": [...] }
}
```
Note `user`/`access` here describe the **staff** identity (effective role/permissions), same as
today's staff response — only the token fields are new. If you need the person's own name/phone
alongside this, that already lives in `user.fullName` (staff's own name), not a separate consumer
profile object; nothing changed there from before this task.

**3. Consumer linked to staff requiring 2FA** (password already verified for both identities in
this branch — personal token is safe to hand over immediately):
```json
{ "requiresTwoFactor": true, "challengeToken": "...", "personalAccessToken": "consumer-jwt" }
```
No `user`/`access`/`workspaceAccessToken` in this response — minimal by design, same as before.
After the client completes `POST /api/auth/2fa/verify` with `challengeToken` (unchanged endpoint,
unchanged `{ accessToken, user }` shape), store that `accessToken` as `workspaceAccessToken`
client-side, alongside the `personalAccessToken` already held from this step.

**4. Legacy staff-only fallback** (no `ConsumerAccount` exists at all — invited employee who
hasn't created a personal account yet):
```json
{
  "personalAccessToken": null,
  "workspaceAccessToken": "staff-jwt",
  "user": {...staff...},
  "access": { "canAccessWorkspace": true, ... }
}
```
If this path also needs 2FA, the challenge response has **no** `personalAccessToken` field at
all (not even `null` — the property is simply absent from the JSON object):
```json
{ "requiresTwoFactor": true, "challengeToken": "..." }
```

## Nothing else moved

`AuthController` / `/api/auth/*` and `ConsumerAuthController` / `/api/consumer-auth/*` are
untouched (out of scope for TASK-496). `/api/auth/2fa/verify` still returns its own
pre-existing `{ accessToken, user }` shape unchanged — that's the source for
`workspaceAccessToken` after a 2FA-gated linked-staff flow (branch 3 above).

## Verification on my end

`dotnet build`: clean. `dotnet test --filter "FullyQualifiedName~MobileAuth"`: 7/7.
`dotnet test --filter "FullyQualifiedName~MobileLoginResponseFactoryTests"`: 3/3 (separate filter
needed — class name doesn't substring-match "MobileAuth"). No files under `mobile/` were touched.
