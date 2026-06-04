# QA Review: TASK-010 + Auth Login
**Date:** 2026-06-03
**Agent:** qa-tester
**Scope:** Login flow (API + UI) + Dashboard page

---

## Test Results

### ✅ API: POST /api/auth/login
- **Result:** PASS (after hash fix)
- Returns `accessToken` + `user` object with correct fields: id, email, fullName, role, tenantId, storeId
- `tenantId: null` correct for provider role
- JWT contains expected claims: sub, email, role, jti, exp

### ✅ API: /api/products (authenticated)
- Not directly tested in this pass — no products seeded yet

### ⚠️ UI: Browser test SKIPPED
- Claude in Chrome extension not connected — UI testing could not be automated
- Manual browser test required at http://localhost:3000/login

---

## Bugs Found

### BUG-001 — Seed hash was incorrect (FIXED)
- **Severity:** high
- **Task:** Auth setup (pre-TASK-003)
- **Steps:** Insert user with hardcoded bcrypt hash → attempt login
- **Expected:** Login succeeds
- **Actual:** "Invalid email or password" — hash `$2a$12$92IXUNpkjO0rOQ5byMi...` (PHP well-known) is not valid for BCrypt.Net-Next
- **Fix applied:** Regenerated hash using actual BCrypt.Net-Next 4.0.3 at workFactor 12. Updated in DB.
- **Credentials:** admin@shelfguard.local / password

---

## Remaining Manual Tests (needs browser)

- [ ] Login form submits and redirects to /dashboard
- [ ] Sidebar renders with all nav items
- [ ] TopBar shows "ShelfGuard" + user name "Admin User"
- [ ] Stats cards render (0 values expected — no products seeded)
- [ ] "Потребують уваги" table renders empty state
- [ ] Quick Actions panel renders
- [ ] Store map zones render (static placeholder data)
- [ ] Sidebar active state highlights /dashboard link
- [ ] Logout button works and redirects to /login
- [ ] Direct navigation to /dashboard without login redirects to /login

---

## Notes
- Dashboard data is derived from /api/products — with empty DB all stats show 0, which is correct empty state
- Store map zones are static placeholder — no backend required
- To seed products for a richer test: POST /api/products with valid payload
