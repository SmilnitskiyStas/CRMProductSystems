# Blocked Tasks

Tasks that cannot proceed due to a blocker.

## TASK-260 — Resend email channel: верифікація домену agrusystems.pp.ua
**Status:** blocked · **Agent:** devops-engineer · **Updated:** 2026-06-19
**Blocker:** DNS-верифікація домену `agrusystems.pp.ua` в Resend ще не завершена (очікування propagation).

**Що вже зроблено:**
- Resend акаунт створено (stassmilnitskiy@gmail.com)
- API ключ додано в `.env` на prod-сервері (`RESEND_API_KEY` + `FROM_EMAIL=noreply@agrusystems.pp.ua`)
- Worker перезапущено з новими змінними
- Тестовий лист через `onboarding@resend.dev` — OK (API ключ валідний)
- Код у `worker/src/services/email.ts` готовий

**Що залишилось:**
1. Додати DNS-записи (SPF, DKIM, DMARC) у DNS-панелі → [resend.com/domains](https://resend.com/domains)
2. Натиснути Verify у Resend
3. Протестувати відправку від `noreply@agrusystems.pp.ua`
4. Перевірити worker logs що email-канал активний

**Unblock:** як тільки домен верифікується — повідомити, email-канал запрацює автоматично.

**Новий залежний (2026-07-30, TASK-455..459):** forgot/reset-password flow (ADR-024) теж
використовує email як основний канал доставки лінка відновлення — і так само чекає на цей
DNS-blocker, щоб email-канал став видимим реальним користувачам. Telegram-fallback (для вже
прив'язаних акаунтів) від TASK-260 не залежить і працює вже сьогодні. Код готовий і чекає з
обох сторін — окремого known-issues запису не створено, це не нова проблема.
