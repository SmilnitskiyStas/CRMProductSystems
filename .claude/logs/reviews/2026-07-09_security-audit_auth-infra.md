# Review: Security Audit — Auth + Infra (pre TASK-329..332)

**Date:** 2026-07-09
**Reviewer:** security-reviewer
**Task:** Повний аудит безпеки автентифікації, реєстрації, БД і серверної інфраструктури
**Result:** failed → план посилення затверджено (TASK-329..332)

## Summary

Базовий рівень добрий: BCrypt(12), refresh-токени в HttpOnly/Secure/SameSite=Strict cookie,
хешовані в БД (SHA256) + ротація на кожен refresh, generic error на логіні, JWT 15 хв
(issuer/audience/lifetime/signature validated, ClockSkew=0), TLS 1.2/1.3 + HSTS/X-Frame-Options/
X-Content-Type-Options у nginx, Swagger тільки в Development, RLS + окремий non-superuser
app-юзер, секрети в .env, login логується в activity_logs.

## Issues Found

### Critical
1. **Немає rate limiting** — ні в API (`Program.cs`), ні в nginx. Brute force на
   `/api/auth/login` нічим не обмежений.
2. **Немає account lockout** — `users` не має failed_login_attempts / lockout_until;
   перебір паролів по одному акаунту необмежений.
3. **Redis (host:6380) і Mosquitto (host:1884, allow_anonymous true) опубліковані на
   всіх інтерфейсах** у `docker-compose.production.yml` без автентифікації. API 5100 /
   web 3100 теж відкриті напряму повз nginx/TLS. Postgres (external, 5434) — аналогічно.

### High
4. **Немає 2FA** — компрометація пароля = повний доступ.
5. **Слабка політика паролів** — тільки `length >= 8` (`UserService.cs:82,187`), без
   перевірки на поширені паролі.
6. **Зміна пароля не відкликає refresh-токени** — викрадена сесія живе далі після
   зміни пароля.
7. **Повторне використання ротованого refresh-токена не детектиться** — просто 401,
   без відкликання сімʼї токенів (ознака викрадення cookie).

### Medium
8. Невдалі спроби логіну не логуються в activity_logs (немає аудиту brute force).
9. Rate limiting відсутній і в nginx (`limit_req`) — auth-ендпоінти нічим не прикриті
   до самого API.
10. Немає CSP / Referrer-Policy / Permissions-Policy заголовків.
11. Немає fail2ban / автоматичних бекапів БД на сервері (скрипти є, cron не активовано).
12. API за nginx, але ForwardedHeaders middleware відсутній — client IP для
    rate limiting / аудиту буде IP проксі.

## Approved for
- Production deploy: no (до виконання TASK-329, 332)

## Follow-up tasks needed
- TASK-329 — backend: rate limiting, lockout, password policy, revoke-on-password-change,
  refresh reuse detection, security headers, ForwardedHeaders, аудит невдалих логінів
- TASK-330 — backend: 2FA TOTP (opt-in) + recovery codes
- TASK-331 — frontend: 2FA UI (login step + profile enrollment), password hints, lockout UX
- TASK-332 — devops: compose localhost bindings, redis auth, nginx limit_req + headers,
  harden-server.sh (ufw/fail2ban/backup cron)
