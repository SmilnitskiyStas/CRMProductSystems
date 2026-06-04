# Skill: Permissions Review

Role matrix from v1-spec.md section 3.2 — verify each endpoint:
- provider: all access
- enterprise_admin: own tenant only
- network_manager: own network
- store_manager: own store
- merchandiser: read + add stock (no approvals)
- storekeeper: receipt + transfer (no write-offs approval)

Key checks:
- Tenant ID never from request body
- store_manager cannot see other stores
- impersonation only for provider role
- impersonation always logged in activity_logs
