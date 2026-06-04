# Known Issues

**Owner:** qa-tester
**Updated:** 2026-06-03

## Active Issues

### KI-001: Backend uses CRM.* project names
Severity: low
Status: open
Description: Test feature created projects named CRM.Api, CRM.Application etc.
Real project uses ShelfGuard.* naming.
Resolution: Rename when starting real v1 implementation.

### KI-002: No authentication implemented
Severity: high
Status: planned (TASK-003)
Description: All endpoints are currently unprotected.
Impact: Cannot deploy to any environment.

### KI-003: Full v1 schema not yet migrated
Severity: high
Status: planned (TASK-002)
Description: Only products table exists. Full v1-spec schema pending.

## Resolved Issues
(none yet)
