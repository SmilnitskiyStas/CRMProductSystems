# TASK-739 — Персонал на сторінці локацій (frontend)

**Дослідження:** прив'язка user↔location вже є: `user_locations`, `GET/PUT /api/users/{id}/locations` (AtLeastEnterpriseAdmin), мультивибір у `InviteUserModal` (для location-scoped ролей) і `UserLocationsEditor` у вкладці «Доступ» картки користувача (тільки enterprise_admin+, не для себе). З боку локації UI не було — додано.

**Зміни:** `features/locations/components/app/(dashboard)/locations/[id]/staff/page.tsx`, посилання «Персонал» у `/locations` (store_manager+ бачить, enterprise_admin+ додає/відкріплює через read-modify-write GET+PUT /users/{id}/locations), i18n uk/en (`Dashboard.locations.staff`, `page.staffButton`). Backend не змінювався.

**Перевірка:** `tsc --noEmit` і `npm run lint` чисті. У браузері не перевірено (немає облікових даних).

**Ризик:** fail-open TASK-401 — працівник location-scoped ролі без жодного призначення бачить усе; поведінку не змінено.
