# PHASE-1-01-PRIVATE-FILES

- **Source IDs / severity:** SEC-002, F06 / P1.
- **Baseline:** `3563ce6c79596ae010f4baebc920d0685f073876`, dirty checkout; current-code revalidation CONFIRMED.
- **Evidence:** ShelfService пише shelf-scans/floor-plans під static root; SupplierAgreementService пише contracts/signatures/stamps; UseStaticFiles не перевіряв tenant/store.
- **Owner approval:** продовжувати по плану, фаза 1. Нові бізнес-правила не вводилися; використано чинні CanViewStock/SupplierCabinet policies і user_locations.
- **Root cause:** storage path/UUID був адресою, а не ACL. Public catalog media та private business assets використовували один static root.
- **Final scope:** private-uploads поза wwwroot для нових файлів; спільний persistent volume для API roles; guard старих static URL; authenticated PrivateFilesController для images; web Blob loader і mobile header-auth Image source; existing agreement-party PDF download збережено.
- **Authorization:** tenant із JWT має збігатися з path tenant; caller subject обов'язковий; stock policy і live user_locations для scans/plans; enterprise_admin/provider bypass store assignment лише всередині свого tenant. Supplier signature/stamp — SupplierCabinet policy + own settings reference. Generic PDF read заборонений.
- **Path safety:** filename/category whitelist; legacy static guard враховує duplicate separators, backslash, case, Windows trailing dot/space та dot traversal; public items лишаються static.
- **Compatibility:** logical DB URLs збережено, серверні PDFs читаються з private root або legacy fallback. Старі files доступні тільки через authorization. Новий floor-plan filename містить location id, щоб authorized preview працював до збереження layout. Старі плани читаються лише за reference в authorized layout. Web/mobile clients оновлені разом.
- **Tests and actual results:** 26 private controller/guard cases, shelf/contract/floor-plan regressions у backend suite 541 PASS; web 28 PASS; mobile 17 PASS; TS/lint PASS. Raw evidence й команди — CHANGELOG_AUDIT.md.
- **Migration:** DB migration не потрібна. Deployment має додати private volume до всіх API roles та включити його в backup inventory. Legacy files фізично не переміщувалися; fallback дає контрольований перехід. Copy/verify/remove legacy files — окремий погоджений operational task, після backup. Не відкривати старі URL під час перенесення.
- **Rollout:** узгоджені API/web/mobile versions; перевірити scans, unsaved/saved/legacy plans, own signature/stamp preview і PDF download обох сторін. Старі mobile clients із direct static image URLs отримуватимуть 404; потрібен план оновлення. CDN/proxy cached private assets і aliases перевірити/очистити під час погодженого rollout.
- **Rollback:** зберегти static deny і private volume. Не запускати старий уразливий image, який відновлює public private assets; forward fix або fail-closed route restriction. Не видаляти volume/legacy originals як rollback.
- **Security/privacy:** tokens тільки в Authorization header; no-store; credentials не відправляються на third-party asset origins; реальні files/PII не читалися.
- **Status:** LOCAL FIX VERIFIED; NOT DEPLOYED; full staging ST-03/T20 та client-device rollout acceptance OPEN.
