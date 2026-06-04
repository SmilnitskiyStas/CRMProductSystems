# Lessons Learned

**Last updated:** 2026-06-03

## 2026-06-03: Initial Setup

### Local PostgreSQL conflicts with Docker
Problem: Docker PostgreSQL on port 5432 conflicted with locally installed PostgreSQL.
Fix: Changed Docker port mapping to 5435.
Lesson: Always check for local services before setting Docker port mappings.
Check: Get-NetTCPConnection -LocalPort 5432 -State Listen

### next.config.ts not supported in Next.js 14
Problem: next.config.ts throws error in Next.js 14 — only supported from v15.
Fix: Rename to next.config.js with module.exports.
Lesson: Check Next.js version before using .ts config.

### Microsoft.EntityFrameworkCore.Design must be in startup project
Problem: EF Core tools failed when Design package was only in Infrastructure project.
Fix: Added Design package to CRM.Api (startup project) with PrivateAssets=all.
Lesson: EF tools need Design package in the --startup-project, not just the --project.

### ASPNETCORE_ENVIRONMENT not set by default with dotnet run
Problem: appsettings.Development.json not loaded — connection string was null.
Fix: Added Properties/launchSettings.json with ASPNETCORE_ENVIRONMENT=Development.
Lesson: Always create launchSettings.json for local dev.
