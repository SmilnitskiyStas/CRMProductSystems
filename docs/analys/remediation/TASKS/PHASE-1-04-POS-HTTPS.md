# PHASE-1-04-POS-HTTPS

- **Source ID / severity:** SEC-014 / P1; CONFIRMED перед fix.
- **Root cause:** URL normalizer приймав remote HTTP; httpRequest також довіряв старій збереженій server URL і follows redirects за default fetch поведінкою.
- **Final change / evidence:** cashier/electron/http.ts:33–74 — HTTPS за default, strict server URL без credentials/query/fragment; перевірка на кожному transport call; redirects=error; TLS errors не деградують до HTTP. cashier/electron/main.ts:133 — loopback HTTP тільки якщо unpackaged і CASHIER_ALLOW_LOOPBACK_HTTP=true. Дозволені local hosts: localhost, 127.0.0.1, ::1; remote/LAN HTTP заборонений навіть з dev opt-in.
- **UI:** activation/checkServer повідомляють про HTTPS requirement. Saved unsafe configs теж блокуються до надсилання header/body. Повертається status=0, тому фінансовий outbox не переводить оплачений receipt у rejected як HTTP 4xx; дані черги не видаляються.
- **Tests:** 16 POS HTTP tests PASS: unsafe URLs, explicit loopback opt-in, saved config refusal before fetch, TLS failure без downgrade, actual redirect refusal без destination request; cashier node+web TS checks PASS. test-results/phase-1-pos-http.xml.
- **Migration/compatibility:** існуючі remote HTTP terminals потребують узгодженого переходу на valid HTTPS і controlled config/reactivation. Зберегти SQLite/outbox та register identity; не скидати дані каси. Для local dev явно встановити CASHIER_ALLOW_LOOPBACK_HTTP=true у unpackaged запуску; packaged build ігнорує цей opt-in.
- **Rollback:** не повертати remote HTTP і не вимикати certificate verification. Доступність виправляти valid TLS endpoint/сертифікатом або forward fix.
- **Residual verification:** real packaged terminal, valid/invalid certificate fixtures і ST-15 device lifecycle/staging QA лишаються OPEN. Прогін mock TLS error не є повним hardware/certificate acceptance.
- **Owner approval:** продовження фази 1. Status: LOCAL FIX VERIFIED; NOT DEPLOYED.
