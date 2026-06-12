---
task_id: TASK-034
date: 2026-06-12
agent: backend-developer
status: done
---

# TASK-034 — Fix 2 failing AuthServiceTests

## Root cause
`IJwtService.GenerateAccessToken` gained a 6th parameter
(`string? fullName = null` — the full_name JWT claim) after the tests were written.
The NSubstitute setup matched only 5 explicit args, so the compiler bound the setup
call with the literal default `fullName: null`, while `AuthService` passes
`user.FullName` ("Test User"). Argument mismatch → substitute returned `""` →
`Expected: "access_token", Actual: ""` in:
- `LoginAsync_returns_tokens_when_credentials_are_valid`
- `RefreshAsync_returns_new_tokens_for_valid_refresh_token`

## Fix (one line)
`AuthServiceTests.cs` setup: added `Arg.Any<string?>()` as the 6th matcher.

## Lesson
Optional parameters are a mock trap: an NSubstitute setup that omits them matches
the *default value*, not "any value". When extending an interface with an optional
param, grep the test mocks for that member.

## Result
Full suite: **249/249 passed** (previously 2 failing since at least 2026-06-11).
