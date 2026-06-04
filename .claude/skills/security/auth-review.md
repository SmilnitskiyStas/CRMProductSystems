# Skill: Auth Review

Checklist:
- JWT issued with: userId, tenantId, role, exp
- Refresh token in HttpOnly cookie (not localStorage)
- Access token short-lived (15 min)
- Refresh token rotated on use
- /auth/me returns current user from JWT, not DB lookup every time
- Failed login: generic error (not 'wrong password' vs 'user not found')
- Rate limiting on /auth/login
