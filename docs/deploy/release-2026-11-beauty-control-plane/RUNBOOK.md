# RUNBOOK: ручний реліз ShelfGuard (Beauty control plane + RLS + тип «Beauty»)

Ціль: вручну (людиною) викотити на прод API (`api_core`, `api_consumer`, `api_provider`) і Web та застосувати 3 міграції БД.
Інтеграція Beauty після релізу ЗАЛИШАЄТЬСЯ ВИМКНЕНОЮ (`BeautyControl__Enabled=false`); env контейнерів, mounts і ресурси не змінюються.

Позначки біля команд:
- **[READ-ONLY]**: нічого не змінює на сервері/в БД.
- **[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН]**: пише файли/образи/контейнери/БД. Перед виконанням перечитай, що саме змінюється.

Усі команди написані для bash (на сервері; на робочій станції, Windows, це Git Bash). Секретів у цьому файлі немає: усі значення це змінні й плейсхолдери `<...>`.
Файли контейнерного env (`before-private.json`, `candidate.private.json`, `rollback.private.json`) МІСТЯТЬ СЕКРЕТИ: ніколи не друкуй їх (`cat`, `docker compose config` без `--quiet`, `docker inspect` без форматування), не копіюй з сервера, не вставляй у чат/тікети.

Супровідні файли в цьому каталозі:
- `upgrade.sql` (+ `upgrade.sql.sha256`): ідемпотентний SQL трьох міграцій (згенеровано `dotnet ef migrations script 20261102102841_AddReceiptCategoryWork --idempotent`).
- `rollback.sql` (+ `rollback.sql.sha256`): Down трьох міграцій у зворотному порядку. НЕ частина штатного релізу, див. розділ 11.
- `tools/make-compose-spec.py`, `tools/verify-containers.py`, `tools.sha256`: допоміжні скрипти (див. кроки 5.11 і 8.3). Це копія логіки `scripts/deploy/receipts-production.py` без прив'язки до попереднього релізу.

---

## 0. Прочитати ПЕРЕД merge PR #30, #31, #32 (блокуюче)

### 0.1 Merge у `main` запускає автодеплой `deploy` у `.github/workflows/ci.yml`

Job `deploy` виконується на кожен push у `main` (після `release-package`), підключається по SSH до прод-сервера й запускає
старий «receipts» конвеєр (`scripts/deploy/receipts-ci-run.py` → `receipts-production.py`). Що я знайшов у цих скриптах:
- Вони зашиті під попередній реліз: `prepare` вимагає, щоб образ `shelfguard-api:p4f-20261101` дорівнював образу працюючого `api_core`; `deploy` вимагає, щоб остання міграція була `20261101104537_AddLabelPrinterProfiles` (на проді вже `20261102102841_AddReceiptCategoryWork`); `upgrade.sql` у payload це `scripts/deploy/receipts-upgrade.sql` (лише receipts-міграція, БЕЗ трьох нових).
- Тому автодеплой, найімовірніше, впаде на asserts ДО зупинки контейнерів. Це висновок із читання коду, на сервері я його не перевіряв. Побічні ефекти навіть при падінні: на сервері створиться каталог `/home/administrator/shelfguard-releases/receipts-<sha12>` і туди скопіюється payload; `prepare` може встигнути виконати ранні кроки.
- Ризик, що він НЕ впаде й виконає застарілий деплой, малий, але його не можна виключити, не бачачи стану сервера.

РІШЕННЯ ЛЮДИНИ (мною не вирішується, скрипти/CI я не змінюю): як поводитись з автодеплоєм при merge. Варіанти: (а) вимкнути/пропустити job `deploy` окремою зміною перед merge; (б) прийняти очікуване падіння (job red) і йти за цим runbook; (в) універсальний деплой за ADR-040 (untracked файл `docs/architecture/ADR-040-universal-release-deploy.md`, я його не чіпав). Поки не вирішено, не зливай PR.

### 0.2 Який артефакт брати

- Три PR зливаються окремо, тому буде до трьох push-ів у `main` і до трьох запусків CI. Артефакт потрібен ТІЛЬКИ для ОСТАННЬОГО merge-коміту (тоді `main` містить усе: міграції, RLS, тип Beauty). Це `MERGE_SHA` нижче.
- Артефакт живе 7 днів від запуску. Якщо реліз відкладається довше, його треба буде перезібрати (повторний запуск workflow).
- `payload.tar.gz` містить також `receipts-production.py`, `receipts-production-rehearsal.py` і `upgrade.sql` (receipts-only). ЦІ ФАЙЛИ НЕ ЗАПУСКАТИ і НЕ використовувати. Використовується лише `api/`, `web/` та `manifest.json` з payload; SQL береться з цього каталогу.
- У `manifest.json` поля `release=receipts-20261102` і `targetMigration=20261102102841_AddReceiptCategoryWork` зашиті в `receipts-ci-package.py`; для цього релізу вони застарілі й це НЕ ознака помилки. Правильність вмісту перевіряється кроками 2.5 і 5.4 (sourceSha + наявність трьох міграцій у DLL).

---

## 1. Передумови

- Доступ: `ssh -p 10048 administrator@93.127.143.98` (ключ у людини), права на `docker` без sudo, `gh` авторизований на робочій станції (`gh auth status`).
- Місце: вільно > 1 ГБ на розділі з `/home/administrator` і ще стільки, скільки займають 2 дампи БД (пробний + основний) + 2 нових образи. Реальний розмір БД дивись у кроці 3.2.
- Вікно простою: вся публічна поверхня (gateway, API, web, worker) недоступна від кроку 6.1 до кроку 9. Орієнтовно: час `pg_dump` + кілька хвилин. Реальний час `pg_dump` заміряй на пробному дампі (крок 4.1).
- Ніхто інший не деплоїть: у CI `concurrency: shelfguard-production`; також переконайся, що жоден workflow не запущений (`gh run list --branch main --limit 3`).
- НЕ запускати `docker image prune`/`docker system prune` до закінчення періоду відкату (старі образи потрібні для відкату за ID).

---

## 2. Етап A (робоча станція): артефакт і SQL

### 2.1 Визначити `MERGE_SHA` (останній merge-коміт у `main`)

**[READ-ONLY]**
```bash
git fetch origin main && git rev-parse origin/main
```

Збережи значення (40 hex) і repo:
```bash
export MERGE_SHA=<40-hex-з-попередньої-команди>; export GH_REPO=SmilnitskiyStas/CRMProductSystems
```

### 2.2 Знайти запуск CI для цього коміту й переконатися, що `release-package` успішний

**[READ-ONLY]**
```bash
gh run list -R "$GH_REPO" --workflow ci.yml --branch main --limit 10 --json databaseId,headSha,status,conclusion --jq '.[] | select(.headSha=="'"$MERGE_SHA"'")'
```
Очікується запис із `status: completed`. `conclusion` може бути `failure` ЛИШЕ через job `deploy` (див. 0.1); це допустимо, якщо `release-package` зелений.

```bash
export RUN_ID=<databaseId-зі-списку>
```

**[READ-ONLY]**
```bash
gh run view "$RUN_ID" -R "$GH_REPO" --json jobs --jq '.jobs[] | [.name, .conclusion] | @tsv'
```
Очікується: `backend-ci`, `frontend-ci`, `worker-ci`, `mobile-ci`, `Package verified release` = `success`. Якщо хоч один червоний, ЗУПИНИСЬ: артефакт з'явиться лише при зеленому `release-package`.

### 2.3 Завантажити артефакт

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: створює локальний каталог]**
```bash
mkdir -p ./beauty-release-2026-11 && gh run download "$RUN_ID" -R "$GH_REPO" -n "receipt-release-$MERGE_SHA" -D ./beauty-release-2026-11/artifact
```

**[READ-ONLY]**
```bash
ls -l ./beauty-release-2026-11/artifact
```
Очікується рівно два файли: `payload.tar.gz`, `payload.sha256`.

### 2.4 Перевірити sha256 ДО копіювання на сервер

**[READ-ONLY]**
```bash
cd ./beauty-release-2026-11/artifact && sha256sum -c payload.sha256
```
Очікується `payload.tar.gz: OK`. Інакше не копіювати.

**[READ-ONLY]** (запиши хеш у запис релізу; він же потрібен у кроці 5 на сервері)
```bash
cut -d ' ' -f 1 payload.sha256
```

### 2.5 Перевірити, що payload зібрано саме з `MERGE_SHA`

**[READ-ONLY]**
```bash
tar -xzOf payload.tar.gz manifest.json | python -I -c "import json,sys; m=json.load(sys.stdin); print(m['sourceSha']); sys.exit(0 if m['sourceSha']=='$MERGE_SHA' else 1)"
```
Очікується `MERGE_SHA` і код 0.

### 2.6 Перевірити SQL і допоміжні файли (з клону репозиторію, у каталозі `docs/deploy/release-2026-11-beauty-control-plane`)

**[READ-ONLY]**
```bash
sha256sum -c upgrade.sql.sha256 rollback.sql.sha256 tools.sha256
```
Очікується `OK` для кожного. (`upgrade.sql` згенеровано з гілки `integrate/beauty-release`; міграції на `main` після merge мають бути ті самі, перевірка DLL у кроці 5.4 це підтвердить.)

**[READ-ONLY]** рівно 3 міграції в `upgrade.sql`:
```bash
grep -o "'2026[0-9]*_[A-Za-z]*'" upgrade.sql | sort -u
```
Очікується рівно:
```
'20261104155307_AddLandingLeadCriticality'
'20261104223156_AddBeautyControlPlane'
'20261105090000_AddBeautyTenantLinksRls'
```

### 2.7 Скопіювати на сервер

Спочатку створи каталог релізу на сервері (див. крок 3.1), потім скопіюй. Змінні каталогу: `SG_RELEASE_DIR=/home/administrator/shelfguard-releases/beauty-cp-<YYYYMMDD>` (каталог НЕ починається з `receipts-`, щоб старі скрипти його не чіпали).

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише файли на прод-сервер, тільки в новий каталог релізу]**
```bash
scp -P 10048 payload.tar.gz payload.sha256 administrator@93.127.143.98:<SG_RELEASE_DIR>/
```

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише файли на прод-сервер, тільки в новий каталог релізу]** (з каталогу `docs/deploy/release-2026-11-beauty-control-plane`)
```bash
scp -P 10048 -r upgrade.sql upgrade.sql.sha256 rollback.sql rollback.sql.sha256 tools tools.sha256 administrator@93.127.143.98:<SG_RELEASE_DIR>/
```

---

## 3. Етап 0 (сервер): read-only інвентаризація і підготовка каталогу

Підключення: **[READ-ONLY]**
```bash
ssh -p 10048 administrator@93.127.143.98
```

### 3.1 Каталог релізу (приватний) і змінні

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: створює новий каталог]**
```bash
umask 077 && export SG_RELEASE_DIR=/home/administrator/shelfguard-releases/beauty-cp-<YYYYMMDD> && mkdir "$SG_RELEASE_DIR" && ls -ld "$SG_RELEASE_DIR"
```
`mkdir` без `-p`: якщо каталог існує, команда впаде (не перезаписуй, розбирайся). Очікується `drwx------`. Якщо ти вже скопіював файли (2.7), створи каталог ДО scp.

Файл змінних і функцій (БЕЗ секретів), підставляє `<...>`:

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише файл у каталог релізу]**
```bash
cat > "$SG_RELEASE_DIR/env.sh" <<'EOF'
export SG_RELEASE_DIR=/home/administrator/shelfguard-releases/beauty-cp-<YYYYMMDD>
export MERGE_SHA=<40-hex>
export PAYLOAD_SHA256=<sha256-з-кроку-2.4>
export REL_TAG=beauty-cp-<YYYYMMDD>
export OLD_API_ID=sha256:c2991e011b720ae879ea06deb943ba4d15b193713cdd216eea7dc29f2d085aba
sgsql() { docker exec -i shelfguard_postgres psql -X -qAt -v ON_ERROR_STOP=1 -U shelfguard -d shelfguard "$@"; }
sgwait() { local n=${1:?name}; local s=; for i in $(seq 1 60); do s=$(docker inspect -f '{{.State.Health.Status}}' "$n" 2>/dev/null); [ "$s" = healthy ] && { echo "$n healthy"; return 0; }; sleep 2; done; echo "$n NOT healthy (last: $s)"; return 1; }
sgcompose() { docker compose -p shelfguard --project-directory "$SG_RELEASE_DIR" -f "$SG_RELEASE_DIR/$1.private.json" up -d --no-build --no-deps "${@:2}"; }
EOF
```

У кожній новій ssh-сесії:
```bash
umask 077 && source /home/administrator/shelfguard-releases/beauty-cp-<YYYYMMDD>/env.sh && cd "$SG_RELEASE_DIR" && pwd
```

### 3.2 Диск і ресурси

**[READ-ONLY]**
```bash
df -h /home /var/lib/docker && free -m
```
Очікується > 1 ГБ вільно на обох розділах (якщо це один розділ, один рядок).

**[READ-ONLY]**
```bash
sgsql -c "SELECT pg_size_pretty(pg_database_size('shelfguard'))"
```
Звір: вільне місце ≥ 2 × (розмір БД) + кілька ГБ під образи.

### 3.3 Контейнери й образи

**[READ-ONLY]**
```bash
docker ps --filter name=shelfguard_ --format 'table {{.Names}}\t{{.Status}}\t{{.Image}}'
```
Очікується 9 рядків: `shelfguard_api_core`, `_api_consumer`, `_api_provider`, `_web`, `_worker`, `_gateway`, `_postgres`, `_redis`, `_mosquitto`, усі `Up`, і `(healthy)` там, де є healthcheck (redis може без healthy-мітки у `Status` залежно від створення).

**[READ-ONLY]**
```bash
for c in api_core api_consumer api_provider web worker gateway postgres redis mosquitto; do docker inspect -f '{{.Name}} {{.State.Status}} {{.State.Health.Status}} {{.Image}}' shelfguard_$c; done
```
Очікується: три `api_*` мають образ `sha256:c2991e011b720ae879ea06deb943ba4d15b193713cdd216eea7dc29f2d085aba`; `web` починається з `sha256:4c7215f5…`. Якщо ні, ЗУПИНИСЬ: стан сервера не той, на який розрахований runbook.

**[READ-ONLY]** (жорстка перевірка api-образу)
```bash
test "$(docker inspect -f '{{.Image}}' shelfguard_api_core shelfguard_api_consumer shelfguard_api_provider | sort -u)" = "$OLD_API_ID" && echo API_IMAGE_OK
```
Очікується `API_IMAGE_OK`.

**[READ-ONLY]**
```bash
docker inspect -f '{{.Image}}' shelfguard_web | grep -c '^sha256:4c7215f5'
```
Очікується `1`.

### 3.4 Порти й env-прапори (лише несекретні ключі)

**[READ-ONLY]** loopback-порт core для внутрішніх проб:
```bash
docker port shelfguard_api_core
```
Очікується рядок на кшталт `5100/tcp -> 127.0.0.1:5110`. Якщо порту немає, у кроці 8.4 використовуй `docker exec ... curl` (там є обидва варіанти).

**[READ-ONLY]** друкує тільки значення двох несекретних ключів (інші змінні не виводяться):
```bash
for c in api_core api_consumer api_provider; do echo "== $c"; docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' shelfguard_$c | grep -E '^(Migrations__RunOnStartup|BeautyControl__Enabled)=' ; done
```
Очікується `Migrations__RunOnStartup=false` для всіх трьох (тоді міграції застосовуємо ми, а не API при старті). `BeautyControl__Enabled` може бути відсутнім (за замовчуванням у коді `false`, див. `BeautyControlOptions`) або `false`. Якщо `Migrations__RunOnStartup` не `false` або `BeautyControl__Enabled=true`, ЗУПИНИСЬ і подзвони людині: це змінює припущення релізу.

**[READ-ONLY]** лише кількість (не значення) секретних ключів Beauty у поточному env:
```bash
docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' shelfguard_api_core | grep -c '^BeautyControl__Secret='
```
Інформаційно (ймовірно `0`).

### 3.5 Стан БД до релізу

**[READ-ONLY]** остання міграція:
```bash
sgsql -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 1'
```
Очікується `20261102102841_AddReceiptCategoryWork`.

**[READ-ONLY]** жодна з трьох нових ще не застосована:
```bash
sgsql -c "SELECT count(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" > '20261102102841_AddReceiptCategoryWork'"
```
Очікується `0`. Якщо `>0`, хтось уже застосував частину (скрипт ідемпотентний, але з'ясуй хто й коли).

**[READ-ONLY]** нової таблиці й колонок ще немає:
```bash
sgsql -c "SELECT to_regclass('public.beauty_tenant_links') IS NULL AS no_table, (SELECT count(*) FROM information_schema.columns WHERE table_name='landing_leads' AND column_name IN ('IsCritical','CriticalReason','ExternalId','BeautyTenantRef','BeautyTenantSlug','Contact','ReporterRole','RequestType')) AS new_cols"
```
Очікується `t|0`.

**[READ-ONLY]** розмір/довжини в `landing_leads` (для оцінки блокувань і ризику відкату Message):
```bash
sgsql -c "SELECT count(*), coalesce(max(length(\"Message\")),0) FROM landing_leads"
```

**[READ-ONLY]** ролі (RLS працює тільки для не-superuser без bypassrls):
```bash
sgsql -c "SELECT rolname, rolsuper, rolbypassrls, rolcreatedb FROM pg_roles WHERE rolname IN ('shelfguard','shelfguard_app') ORDER BY 1"
```
Очікується `shelfguard_app|f|f|...`. Роль `shelfguard` (від якої застосовується SQL) може бути superuser або власником схеми; запам'ятай значення.

**[READ-ONLY]** власники таблиць і привілеї app-ролі (для передбачення доступу до нової таблиці, див. 7.3):
```bash
sgsql -c "SELECT tablename, tableowner, has_table_privilege('shelfguard_app', format('public.%I', tablename), 'SELECT,INSERT,UPDATE,DELETE') AS app_rw FROM pg_tables WHERE schemaname='public' AND tablename IN ('tenants','landing_leads','label_template_revisions','stock_receipts') ORDER BY 1"
```
Запиши власника й `app_rw`. Нові таблиці, створені через `psql -U shelfguard`, належатимуть `shelfguard` (якщо немає default privileges, див. наступну команду).

**[READ-ONLY]**
```bash
sgsql -c "SELECT defaclrole::regrole, defaclobjtype, defaclacl FROM pg_default_acl"
```
Інформаційно: чи налаштовані default privileges для нових таблиць.

### 3.6 Базова лінія HTTP ДО релізу

**[READ-ONLY]** (з сервера; жодних тіл запитів, лише коди й `X-Served-By`)
```bash
probe() { printf '%-62s ' "$1"; curl -s -o /dev/null --max-time 15 -D - ${2:+-X "$2"} ${3:+-H 'Content-Type: application/json' -d "$3"} "https://api.agrusystems.pp.ua$1" | awk 'NR==1{c=$2} tolower($1)=="x-served-by:"{r=$2} END{print c, r}'; }; probe /health/ready; probe /api/auth/me; probe /api/consumer/profile; probe /api/supplier-cabinet/profile; probe /api/provider/leads; probe /api/provider/beauty/tenants/00000000-0000-0000-0000-000000000001; probe /api/integrations/beauty/feedback POST '{}'
```
Очікується ДО релізу: `/health/ready 200 core`; `/api/auth/me 401 core`; `/api/consumer/profile 401 consumer`; `/api/supplier-cabinet/profile 401 provider`; `/api/provider/leads 401 provider`. Два Beauty-ендпоінти ДО релізу `404` (їх у поточному коді ще немає); після релізу вони мають стати `401` (це доказ, що нова версія працює). Збережи цей висновок.

**[READ-ONLY]**
```bash
for p in /login /reset-password; do printf '%s ' "$p"; curl -s -o /dev/null -w '%{http_code}\n' --max-time 20 "https://agrusystems.pp.ua$p"; done
```
Очікується `200` для обох.

### 3.7 Заморозити поточний стан контейнерів (для специфікацій і перевірки незмінності)

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише файл із СЕКРЕТАМИ (env контейнерів) у каталог релізу, права 0600; НЕ друкувати]**
```bash
docker inspect shelfguard_api_core shelfguard_api_consumer shelfguard_api_provider shelfguard_web shelfguard_worker shelfguard_gateway shelfguard_postgres shelfguard_redis shelfguard_mosquitto > "$SG_RELEASE_DIR/before-private.json" && chmod 600 "$SG_RELEASE_DIR/before-private.json" && ls -l "$SG_RELEASE_DIR/before-private.json"
```

**[READ-ONLY]** (опис не секретний; для запису релізу)
```bash
for c in api_core api_consumer api_provider web worker gateway postgres redis mosquitto; do docker inspect -f '{{.Name}} id={{printf "%.12s" .Id}} image={{.Image}} cfgimage={{.Config.Image}}' shelfguard_$c; done | tee "$SG_RELEASE_DIR/containers-before.txt"
```

---

## 4. Етап 1: пробний бекап і репетиція SQL на ізольованій копії (до простою, рекомендовано)

Мета: переконатися, що дамп читається, що `upgrade.sql` проходить двічі (ідемпотентність) на копії реальних даних, і заміряти час `pg_dump`. Прод-БД лише читається (звичайний `pg_dump`). Репетиція йде в окремому тимчасовому контейнері без мережі назовні й без портів.

### 4.1 Пробний дамп (без простою)

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише дамп із даними прод-БД у каталог релізу (0600), дає навантаження на БД]**
```bash
time docker exec shelfguard_postgres pg_dump -U shelfguard -d shelfguard -Fc > "$SG_RELEASE_DIR/rehearsal.dump" && ls -l "$SG_RELEASE_DIR/rehearsal.dump"
```
Очікується ненульовий розмір. Запиши `real` час: це оцінка тривалості простою на основний дамп.

**[READ-ONLY]** повне прочитання архіву (ловить пошкодження):
```bash
docker exec -i shelfguard_postgres pg_restore --file=/dev/null < "$SG_RELEASE_DIR/rehearsal.dump" && echo DUMP_READABLE
```
Очікується `DUMP_READABLE`.

### 4.2 Тимчасова ізольована БД тієї ж версії PostgreSQL

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: створює тимчасову docker-мережу (internal) і контейнер; прод-контейнери не зачіпаються]**
```bash
PGIMG=$(docker inspect -f '{{.Config.Image}}' shelfguard_postgres) && docker network create --internal sg-beauty-rehearsal >/dev/null && docker run -d --name sg-beauty-rehearsal-db --network sg-beauty-rehearsal --memory 512m -e POSTGRES_USER=shelfguard -e POSTGRES_DB=shelfguard -e POSTGRES_HOST_AUTH_METHOD=trust "$PGIMG" -c max_connections=20 && sleep 10 && docker exec sg-beauty-rehearsal-db pg_isready -U shelfguard
```
Очікується `accepting connections`. (Образ береться той самий, що в проді, тому тягнути з інтернету нічого не треба; без паролів, бо мережа internal і портів немає.)

### 4.3 Відновлення дампу в копію

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: лише в тимчасовому контейнері]**
```bash
docker exec -i sg-beauty-rehearsal-db pg_restore --exit-on-error --no-owner --no-privileges -U shelfguard -d shelfguard < "$SG_RELEASE_DIR/rehearsal.dump" && echo RESTORED
```
Очікується `RESTORED`. Примітка: з `--no-owner --no-privileges` репетиція НЕ перевіряє власників і GRANT-и (їх перевіряє крок 7.3 на проді).

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: лише в тимчасовому контейнері]** роль додатка, як у попередній репетиції:
```bash
docker exec -i sg-beauty-rehearsal-db psql -X -v ON_ERROR_STOP=1 -U shelfguard -d shelfguard -c "CREATE ROLE shelfguard_app NOSUPERUSER NOBYPASSRLS; GRANT USAGE ON SCHEMA public TO shelfguard_app;"
```

### 4.4 `upgrade.sql` двічі на копії

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: лише в тимчасовому контейнері]** перший запуск:
```bash
docker exec -i sg-beauty-rehearsal-db psql -X -v ON_ERROR_STOP=1 -U shelfguard -d shelfguard < "$SG_RELEASE_DIR/upgrade.sql" > "$SG_RELEASE_DIR/rehearsal-upgrade-1.log" 2>&1; echo rc=$?
```
Очікується `rc=0` (у логу є `NOTICE ... policy ... does not exist, skipping`: нормально).

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: лише в тимчасовому контейнері]** другий запуск (ідемпотентність):
```bash
docker exec -i sg-beauty-rehearsal-db psql -X -v ON_ERROR_STOP=1 -U shelfguard -d shelfguard < "$SG_RELEASE_DIR/upgrade.sql" > "$SG_RELEASE_DIR/rehearsal-upgrade-2.log" 2>&1; echo rc=$?
```
Очікується `rc=0`, без нових помилок.

**[READ-ONLY]** результат на копії:
```bash
docker exec -i sg-beauty-rehearsal-db psql -X -qAt -U shelfguard -d shelfguard -c "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1 DESC LIMIT 4" -c "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid='public.beauty_tenant_links'::regclass"
```
Очікується 4 міграції (`20261105090000_AddBeautyTenantLinksRls`, `20261104223156_AddBeautyControlPlane`, `20261104155307_AddLandingLeadCriticality`, `20261102102841_AddReceiptCategoryWork`) і `t|t`.

### 4.5 Прибрати репетицію

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: видаляє лише тимчасові ресурси `sg-beauty-rehearsal-*`]**
```bash
docker rm -f sg-beauty-rehearsal-db && docker network rm sg-beauty-rehearsal
```

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: видаляє пробний дамп із даними прод-БД]**
```bash
shred -u "$SG_RELEASE_DIR/rehearsal.dump" 2>/dev/null || rm -f "$SG_RELEASE_DIR/rehearsal.dump"
```
(Основний дамп робиться в кроці 6.2 уже при зупинених сервісах.)

---

## 5. Етап 2: артефакт на сервері, збірка candidate-образів

### 5.1 Перевірити sha256 payload на сервері

**[READ-ONLY]**
```bash
cd "$SG_RELEASE_DIR" && sha256sum payload.tar.gz | cut -d ' ' -f 1 | grep -x "$PAYLOAD_SHA256" && echo PAYLOAD_SHA_OK
```
Очікується `PAYLOAD_SHA_OK` (хеш з кроку 2.4 на робочій станції збігається з серверним).

### 5.2 Розпакувати у НОВИЙ порожній каталог контексту (з перевіркою шляхів)

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише файли в `$SG_RELEASE_DIR/ctx`]**
```bash
mkdir "$SG_RELEASE_DIR/ctx" && python3 -I -c "import tarfile,pathlib,sys; P=pathlib.PurePosixPath; t=tarfile.open(sys.argv[1]); ms=t.getmembers(); assert all(not m.issym() and not m.islnk() and not P(m.name).is_absolute() and '..' not in P(m.name).parts for m in ms); t.extractall(sys.argv[2], filter='data'); print('extracted', len(ms))" "$SG_RELEASE_DIR/payload.tar.gz" "$SG_RELEASE_DIR/ctx"
```
Якщо `python3` старіший і падає на `filter='data'` (потрібен 3.12+ або 3.8.17+/3.9.17+/3.10.12+/3.11.4+), повідом людині; попередній реліз використовував той самий виклик, тож на сервері він має працювати.

### 5.3 Перевірити манифест: хеші всіх файлів і `sourceSha`

**[READ-ONLY]**
```bash
python3 -I -c "import json,hashlib,pathlib,sys; r=pathlib.Path(sys.argv[1]); m=json.loads((r/'manifest.json').read_text()); bad=[n for n,h in m['files'].items() if hashlib.sha256((r/n).read_bytes()).hexdigest()!=h]; print('sourceSha',m['sourceSha'],'files',len(m['files']),'bad',bad); sys.exit(1 if bad or m['sourceSha']!=sys.argv[2] else 0)" "$SG_RELEASE_DIR/ctx" "$MERGE_SHA" && echo MANIFEST_OK
```
Очікується `bad []` і `MANIFEST_OK`.

### 5.4 Перевірити, що збірка містить усі 3 нові міграції (збіг з `upgrade.sql`)

**[READ-ONLY]**
```bash
python3 -I -c "import sys; d=open(sys.argv[1],'rb').read(); r={i:(i.encode() in d or i.encode('utf-16le') in d) for i in sys.argv[2:]}; print(r); sys.exit(0 if all(r.values()) else 1)" "$SG_RELEASE_DIR/ctx/api/ShelfGuard.Infrastructure.dll" 20261104155307_AddLandingLeadCriticality 20261104223156_AddBeautyControlPlane 20261105090000_AddBeautyTenantLinksRls && echo MIGRATIONS_IN_DLL
```
Очікується `MIGRATIONS_IN_DLL`. Якщо ні, це не той артефакт (не з останнього merge-коміту): ЗУПИНИСЬ.

### 5.5 Прибрати з контексту старі receipts-скрипти (щоб випадково не запустити)

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: видаляє файли лише з `ctx`]**
```bash
rm -f "$SG_RELEASE_DIR"/ctx/receipts-production.py "$SG_RELEASE_DIR"/ctx/receipts-production-rehearsal.py "$SG_RELEASE_DIR"/ctx/upgrade.sql && ls "$SG_RELEASE_DIR/ctx"
```
Очікується: `api`, `web`, `manifest.json`, `backend-acceptance.json`, `web-acceptance.json`.

### 5.6 Закріпити поточні образи тегами (опора для відкату й для `FROM`)

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: створює теги на існуючі образи (нічого не видаляє; теги також захищають образи від prune)]**
```bash
export OLD_WEB_ID=$(docker inspect -f '{{.Image}}' shelfguard_web) && docker tag "$OLD_API_ID" "shelfguard-api:pre-$REL_TAG" && docker tag "$OLD_WEB_ID" "shelfguard-web:pre-$REL_TAG" && echo "OLD_WEB_ID=$OLD_WEB_ID"
```
`OLD_WEB_ID` буде дописано в `env.sh` у кроці 5.10 (разом з новими ID). Якщо сесія обірветься раніше, значення є в `containers-before.txt` (поле `image=` рядка `shelfguard_web`).

### 5.7 Dockerfile-и й `.dockerignore`

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише 3 файли в `ctx`]** (як у попередньому релізі: шар `COPY` поверх запущеного образу, без мережі)
```bash
cd "$SG_RELEASE_DIR/ctx" && printf 'FROM shelfguard-api:pre-%s\nWORKDIR /app\nCOPY api/ /app/\nRUN rm -f /app/appsettings.Production.json /app/appsettings.Development.json /app/appsettings.Staging.json\nENTRYPOINT ["dotnet", "ShelfGuard.Api.dll"]\n' "$REL_TAG" > Dockerfile.api && printf 'FROM shelfguard-web:pre-%s\nWORKDIR /app\nRUN rm -rf /app/.next\nCOPY web/.next/ /app/.next/\nCOPY web/public/ /app/public/\n' "$REL_TAG" > Dockerfile.web && printf '*\n!api/\n!api/**\n!web/\n!web/**\n!Dockerfile.api\n!Dockerfile.web\n' > .dockerignore && cat Dockerfile.api Dockerfile.web
```

### 5.8 Зібрати образ API

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: створює новий docker-образ; запущені контейнери не змінюються]**
```bash
cd "$SG_RELEASE_DIR/ctx" && docker build --network none --pull=false -f Dockerfile.api -t "shelfguard-api:$REL_TAG" . > "$SG_RELEASE_DIR/image-build-api.log" 2>&1; echo rc=$?
```
Очікується `rc=0`. Якщо ні, дивись хвіст логу: `tail -n 30 "$SG_RELEASE_DIR/image-build-api.log"`.

### 5.9 Зібрати образ Web

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: створює новий docker-образ]**
```bash
cd "$SG_RELEASE_DIR/ctx" && docker build --network none --pull=false -f Dockerfile.web -t "shelfguard-web:$REL_TAG" . > "$SG_RELEASE_DIR/image-build-web.log" 2>&1; echo rc=$?
```
Очікується `rc=0`.

### 5.10 Записати ID нових образів

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: дописує у `env.sh` (несекретно)]**
```bash
NEW_API_ID=$(docker image inspect "shelfguard-api:$REL_TAG" --format '{{.Id}}') && NEW_WEB_ID=$(docker image inspect "shelfguard-web:$REL_TAG" --format '{{.Id}}') && printf 'export NEW_API_ID=%s\nexport NEW_WEB_ID=%s\nexport OLD_WEB_ID=%s\n' "$NEW_API_ID" "$NEW_WEB_ID" "$OLD_WEB_ID" >> "$SG_RELEASE_DIR/env.sh" && source "$SG_RELEASE_DIR/env.sh" && echo "api $NEW_API_ID" && echo "web $NEW_WEB_ID"
```
Очікується два ID, відмінні від `c2991e01…` і `4c7215f5…`.

**[READ-ONLY]** швидка перевірка вмісту нового API-образу без запуску застосунку (ентрипойнт перевизначено):
```bash
docker run --rm --network none --entrypoint ls "$NEW_API_ID" -l /app/ShelfGuard.Api.dll /app/ShelfGuard.Infrastructure.dll
```

### 5.11 Згенерувати candidate/rollback специфікації з фактичних контейнерів

Скрипт копіює env, mounts, мережі, порти, healthcheck і ліміти 1:1 з `before-private.json`; змінюється тільки `image`. Нічого не зупиняє й не запускає.

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише `candidate.private.json` і `rollback.private.json` (СЕКРЕТИ, 0600), нічого не друкує з env]**
```bash
python3 -I "$SG_RELEASE_DIR/tools/make-compose-spec.py" "$SG_RELEASE_DIR/before-private.json" "$SG_RELEASE_DIR" "$NEW_API_ID" "$NEW_WEB_ID"
```
Очікується: рядки `candidate: written ...`, `rollback: written ...` (з образами `sha256:c2991e01…` у rollback), три `NOTE ...: Migrations__RunOnStartup=false`. Якщо скрипт кинув `AssertionError` («Environment changed by Compose interpolation»), ЗУПИНИСЬ: спец змінила б env.

**[READ-ONLY]** валідація (тихо; без `--quiet` команда надрукувала б секрети!):
```bash
docker compose -p shelfguard -f "$SG_RELEASE_DIR/candidate.private.json" config --quiet && docker compose -p shelfguard -f "$SG_RELEASE_DIR/rollback.private.json" config --quiet && echo SPECS_VALID
```

---

## 6. Етап 3: простій (quiesce) і основний бекап

Від цього кроку сайт/API недоступні. Старі контейнери ЗУПИНЕНІ, але ще НЕ видалені: до кроку 8.1 відкат це просто `docker start` (11.2).

### 6.1 Зупинити gateway і worker, потім API та web

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: ПОЧАТОК ПРОСТОЮ. Зупиняє gateway, worker, web, три API. postgres/redis/mosquitto не чіпаємо]**
```bash
date -u +%FT%TZ | tee "$SG_RELEASE_DIR/downtime-start.txt" && docker stop shelfguard_gateway shelfguard_worker shelfguard_web shelfguard_api_core shelfguard_api_consumer shelfguard_api_provider
```
(Контейнери з `restart: unless-stopped`, зупинені вручну, самі не піднімуться.)

**[READ-ONLY]** клієнтів БД не лишилось:
```bash
sgsql -c "SELECT count(*) FROM pg_stat_activity WHERE datname='shelfguard' AND pid<>pg_backend_pid() AND backend_type='client backend'"
```
Очікується `0` (або лише сторонні клієнти, яких ти впізнаєш; з'ясуй, що це).

### 6.2 Основний (quiesced) дамп

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: пише основний бекап (0600). Це єдина точка відкату БД]**
```bash
test ! -e "$SG_RELEASE_DIR/database-quiesced-before.dump" && docker exec shelfguard_postgres pg_dump -U shelfguard -d shelfguard -Fc > "$SG_RELEASE_DIR/database-quiesced-before.dump" && ls -l "$SG_RELEASE_DIR/database-quiesced-before.dump"
```

**[READ-ONLY]** перевірка читабельності (повне читання + зміст):
```bash
docker exec -i shelfguard_postgres pg_restore --file=/dev/null < "$SG_RELEASE_DIR/database-quiesced-before.dump" && echo DUMP_READABLE && docker exec -i shelfguard_postgres pg_restore --list < "$SG_RELEASE_DIR/database-quiesced-before.dump" | wc -l
```
Очікується `DUMP_READABLE` і кількість записів TOC (сотні). Якщо помилка: не продовжуй, повтори дамп; при необхідності повернути сервіси `docker start` (крок 11.2).

**[READ-ONLY]** контрольна сума:
```bash
sha256sum "$SG_RELEASE_DIR/database-quiesced-before.dump" | tee "$SG_RELEASE_DIR/database-quiesced-before.dump.sha256"
```

### 6.3 Зберегти бекап поза сервером

Дамп на тому самому сервері не захищає від втрати сервера. З робочої станції, у сховище, яке ти контролюєш:

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: копіює дамп (дані клієнтів!) на робочу станцію; став шифрований диск/сховище]**
```bash
scp -P 10048 administrator@93.127.143.98:<SG_RELEASE_DIR>/database-quiesced-before.dump* <локальний-захищений-каталог>/
```
Після копіювання перевір: `sha256sum -c database-quiesced-before.dump.sha256` (на станції; у файлі абсолютний/відносний шлях може відрізнятися: порівняй хеш вручну).

---

## 7. Етап 4: застосування `upgrade.sql`

### 7.1 Застосувати

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: змінює схему прод-БД (адитивно, див. розділ 12)]**
```bash
docker exec -i shelfguard_postgres psql -X -v ON_ERROR_STOP=1 -U shelfguard -d shelfguard < "$SG_RELEASE_DIR/upgrade.sql" > "$SG_RELEASE_DIR/upgrade.sql.apply.log" 2>&1; echo rc=$?
```
Очікується `rc=0`. У логу будуть три `NOTICE: policy ... does not exist, skipping`: нормально.

Про `-1` (`--single-transaction`): у запиті було `psql ... -1`, але я НЕ використовую його свідомо. Файл, який генерує EF, уже містить власні `START TRANSACTION ... COMMIT` для кожної міграції. З `-1` psql видає `WARNING: there is already a transaction in progress`, перший `COMMIT` фіксує все накопичене, а останній `COMMIT` від `-1` дає `no transaction in progress`: тобто додаткової атомарності немає (перевірено локально на PostgreSQL 16). Фактична атомарність: кожна з трьох міграцій окремо атомарна; `ON_ERROR_STOP=1` зупиняє psql на першій помилці, відкритa транзакція відкочується при закритті з'єднання; уже закомічені попередні міграції лишаються, а повторний запуск після виправлення причини безпечний (ідемпотентність).

**[READ-ONLY]**
```bash
cat "$SG_RELEASE_DIR/upgrade.sql.apply.log"
```
Не повинно бути рядків `ERROR`.

### 7.2 Перевірити історію міграцій

**[READ-ONLY]**
```bash
sgsql -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY 1 DESC LIMIT 4'
```
Очікується (зверху вниз): `20261105090000_AddBeautyTenantLinksRls`, `20261104223156_AddBeautyControlPlane`, `20261104155307_AddLandingLeadCriticality`, `20261102102841_AddReceiptCategoryWork`.

### 7.3 Перевірити схему, RLS і доступ app-ролі

**[READ-ONLY]** RLS на `beauty_tenant_links` (обидва прапорці мають бути `t`) і політики:
```bash
sgsql -c "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid='public.beauty_tenant_links'::regclass" -c "SELECT policyname FROM pg_policies WHERE tablename='beauty_tenant_links' ORDER BY 1"
```
Очікується `t|t` і три політики: `provider_bypass`, `tenant_isolation`, `worker_bypass`.

**[READ-ONLY]** колонки й індекси:
```bash
sgsql -c "SELECT column_name FROM information_schema.columns WHERE table_name='landing_leads' AND column_name IN ('IsCritical','CriticalReason','ExternalId','BeautyTenantRef','BeautyTenantSlug','Contact','ReporterRole','RequestType') ORDER BY 1" -c "SELECT indexname FROM pg_indexes WHERE indexname IN ('IX_landing_leads_ExternalId','IX_beauty_tenant_links_BeautySlug','IX_beauty_tenant_links_TenantId') ORDER BY 1"
```
Очікується 8 колонок і 3 індекси.

**[READ-ONLY]** доступ app-ролі (ВАЖЛИВО: нова таблиця створена роллю `shelfguard`; застосунок працює як `shelfguard_app`):
```bash
sgsql -c "SELECT tableowner, has_table_privilege('shelfguard_app','public.beauty_tenant_links','SELECT,INSERT,UPDATE,DELETE') AS app_rw FROM pg_tables WHERE tablename='beauty_tenant_links'" -c "SELECT has_column_privilege('shelfguard_app','public.landing_leads','IsCritical','SELECT') AND has_column_privilege('shelfguard_app','public.landing_leads','IsCritical','INSERT') AS leads_cols_ok"
```
Очікується `app_rw = t` і `leads_cols_ok = t`. Порівняй `tableowner` з власником існуючих таблиць із кроку 3.5.
Якщо `app_rw = f`: це відомий клас помилок (KI про ownership/GRANT, `.claude/docs/database-schema.md`, «Lesson from TASK-411»). Таблиця використовується лише за ввімкненої інтеграції Beauty та в ProviderOnly-ендпоінтах Beauty, тож решту системи це не зламає, але це треба виправити ДО ввімкнення Beauty. Виправлення змінює БД, тому рішення за людиною: узгодити з власником існуючих таблиць (`ALTER TABLE public.beauty_tenant_links OWNER TO <власник-таблиці-tenants>` або `GRANT SELECT, INSERT, UPDATE, DELETE ON public.beauty_tenant_links TO shelfguard_app`). Я цей крок не виконую й не перевіряв на проді.

---

## 8. Етап 5: заміна контейнерів api_core / api_consumer / api_provider / web

Тільки ці 4 сервіси. Worker, gateway, postgres, redis, mosquitto не перестворюються.

### 8.1 Підняти candidate

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: ВИДАЛЯЄ старі контейнери цих 4 сервісів і створює нові з candidate-образами; після цього відкат лише за розділом 11]**
```bash
sgcompose candidate api_core api_consumer api_provider web > "$SG_RELEASE_DIR/candidate-compose.log" 2>&1; echo rc=$?
```
Очікується `rc=0`.

### 8.2 Дочекатися healthy

**[READ-ONLY]**
```bash
for c in api_core api_consumer api_provider web; do sgwait shelfguard_$c || break; done
```
Очікується чотири `healthy`. Якщо `NOT healthy` після ~2 хв: логи `docker logs --tail 80 shelfguard_<name>` і критерії відкату (розділ 11).

### 8.3 Перевірка, що env/mounts/ресурси не змінилися, а образи нові

**[READ-ONLY]**
```bash
python3 -I "$SG_RELEASE_DIR/tools/verify-containers.py" "$SG_RELEASE_DIR/before-private.json" "$NEW_API_ID" "$NEW_WEB_ID"; echo rc=$?
```
Очікується `OK` для всіх 9 контейнерів і `rc=0`. Рядки `DIFF ...` означають зміну (env, mounts, ліміти) або зміну нецільового контейнера: це привід для відкату. Команда не друкує значення env.

### 8.4 Внутрішні проби ДО відкриття трафіку (gateway ще зупинений)

**[READ-ONLY]** core через loopback-порт (якщо `docker port` у 3.4 показав 5110):
```bash
for p in /health/live /health/ready /api/auth/me; do printf '%s ' "$p"; curl -s -o /dev/null -w '%{http_code}\n' --max-time 15 "http://127.0.0.1:5110$p"; done
```
Очікується `200`, `200`, `401`. Якщо loopback-порту немає, виконай те саме через `docker exec shelfguard_api_core curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5100/health/ready`.

**[READ-ONLY]** Beauty-ендпоінти на core без підпису (нова версія): мають відповісти 401 (HMAC), а не 404:
```bash
docker exec shelfguard_api_core curl -s -o /dev/null -w '%{http_code}\n' -X POST -H 'Content-Type: application/json' -d '{}' http://127.0.0.1:5100/api/integrations/beauty/feedback
```
Очікується `401`.

**[READ-ONLY]** provider і consumer:
```bash
for r in provider consumer; do printf '%s ' $r; docker exec shelfguard_api_$r curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5100/health/live; done
```
Очікується `200` для обох.

**[READ-ONLY]** provider: Beauty-ендпоінт провайдера без токена:
```bash
docker exec shelfguard_api_provider curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:5100/api/provider/beauty/tenants/00000000-0000-0000-0000-000000000001
```
Очікується `401`.

**[READ-ONLY]** ознак проблем у логах старту (дивись, чи немає застосування міграцій і виняткових стеків):
```bash
for c in api_core api_consumer api_provider; do echo "== $c"; docker logs --since 10m shelfguard_$c 2>&1 | grep -iE 'error|exception|fail|Applying migration' | head -n 10; done
```
Очікується порожньо або безпечні попередження. Рядків `Applying migration` бути не повинно (`RunOnStartup=false`, усе вже застосовано).

---

## 9. Етап 6: розморозити worker і gateway

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: КІНЕЦЬ ПРОСТОЮ. Запускає gateway і worker (відкриває трафік)]**
```bash
docker start shelfguard_gateway shelfguard_worker && sgwait shelfguard_gateway && sgwait shelfguard_worker; date -u +%FT%TZ | tee "$SG_RELEASE_DIR/downtime-end.txt"
```
Очікується `shelfguard_gateway healthy` і `shelfguard_worker healthy`.

**[READ-ONLY]** worker не скаржиться на з'єднання з API (не публікуй вивід назовні):
```bash
docker logs --since 3m shelfguard_worker 2>&1 | tail -n 30
```
Очікується без `ECONNREFUSED`/`401`/`500` у циклі. Поодинокі повтори одразу після старту допустимі.

---

## 10. Перевірка після розморожування

### 10.1 HTTP-проби (read-only, з сервера або станції)

**[READ-ONLY]**
```bash
probe() { printf '%-62s ' "$1"; curl -s -o /dev/null --max-time 15 -D - ${2:+-X "$2"} ${3:+-H 'Content-Type: application/json' -d "$3"} "https://api.agrusystems.pp.ua$1" | awk 'NR==1{c=$2} tolower($1)=="x-served-by:"{r=$2} END{print c, r}'; }; probe /health/live; probe /health/ready; probe /api/auth/me; probe /api/pos/sync/catalog-v2; probe /api/consumer/profile; probe /api/supplier-cabinet/profile; probe /api/provider/leads; probe /api/internal/worker-session; probe /api/provider/beauty/tenants/00000000-0000-0000-0000-000000000001; probe /api/integrations/beauty/feedback POST '{}'
```
Очікувані коди та роль у `X-Served-By`:

| Шлях | Код | Роль |
|---|---|---|
| `/health/live`, `/health/ready` | 200 | core |
| `/api/auth/me` | 401 | core |
| `/api/pos/sync/catalog-v2` | 401 | core |
| `/api/consumer/profile` | 401 | consumer |
| `/api/supplier-cabinet/profile` | 401 | provider |
| `/api/provider/leads` | 401 | provider |
| `/api/internal/worker-session` | 404 (заблоковано на gateway) | (немає) |
| `GET /api/provider/beauty/tenants/<guid>` | 401 (було 404 до релізу) | provider |
| `POST /api/integrations/beauty/feedback` без HMAC | 401 `control_auth_failed` (було 404 до релізу) | core |

Beauty-ендпоінти відповідають 401, а не успіхом, бо без підпису/токена; інтеграція вимкнена (`BeautyControl__Enabled=false`), контрольний канал назовні не викликається. Жодних POST із реальними даними (зокрема на `/api/public/leads`) не робити: це створило б прод-дані.

**[READ-ONLY]** веб:
```bash
for p in /login /reset-password; do printf '%s ' "$p"; curl -s -o /dev/null -w '%{http_code}\n' --max-time 20 "https://agrusystems.pp.ua$p"; done
```
Очікується `200` для обох.

**[READ-ONLY]** Web віддає нову збірку (BUILD_ID з payload):
```bash
docker exec shelfguard_web cat /app/.next/BUILD_ID && cat "$SG_RELEASE_DIR/ctx/web/.next/BUILD_ID"
```
Очікується два однакові значення.

### 10.2 Перевірка логіну та шляху через БД

**[READ-ONLY]** запит з неіснуючим користувачем проходить через БД/RLS і має дати клієнтську помилку, не 5xx:
```bash
curl -s -o /dev/null -w '%{http_code}\n' --max-time 15 -X POST -H 'Content-Type: application/json' -d '{"email":"nobody@example.invalid","password":"x"}' https://api.agrusystems.pp.ua/api/auth/login
```
Очікується 4xx (як правило 401). `5xx` це привід для відкату. (Поле тіла я не звіряв з контролером; якщо отримаєш `400`, це теж нормально: головне не 5xx.)

Вручну в браузері (людина, свої облікові дані; паролі ніде не записувати):
1. https://agrusystems.pp.ua: вхід користувачем магазину, відкрити склад і POS-продаж (перегляд).
2. Вхід провайдером: відкрити панель провайдера і розділ заявок (leads). Це читає `landing_leads` з новими колонками (`IsCritical`, ...) і через `api_provider`. Список відкривається без помилки.
3. Mobile (consumer): застосунок завантажується і логіниться (маршрути `api_consumer`).
4. Нічого не змінювати/не створювати в Beauty-розділах (інтеграція вимкнена).

### 10.3 Критерії успіху

Реліз успішний, якщо ВСЕ виконано:
- `upgrade.sql` застосовано без `ERROR`; чотири останні записи історії відповідають 7.2.
- `beauty_tenant_links`: `relrowsecurity=t`, `relforcerowsecurity=t`, 3 політики; `app_rw=t` (або дефект GRANT усвідомлено й заведено, 7.3).
- `api_core`, `api_consumer`, `api_provider`, `web`, `worker`, `gateway` усі `healthy`; `verify-containers.py` дає `rc=0` (env, mounts, ліміти ідентичні, нецільові контейнери без змін).
- HTTP-проби 10.1 відповідають таблиці; Beauty-ендпоінти повертають 401, а не 404/5xx.
- Логін і панель провайдера працюють; у логах API/worker немає хвилі 5xx протягом 10 хв після розморожування.
- `BeautyControl__Enabled` не змінювався (за замовчуванням false).

---

## 11. Відкат

### 11.1 Коли відкочувати

Відкочуй КОНТЕЙНЕРИ, якщо:
- будь-який з `api_*`/`web` не став `healthy` за ~3 хв після кроку 8.1;
- `verify-containers.py` показує `DIFF` (змінений env/mount/ліміт, нецільовий контейнер змінено);
- пробa `/health/ready` ≠ 200, логін/POS дають 5xx, повторні 5xx на `/api/*` після розморожування, OOM/рестарт-цикл (`docker ps`, `dmesg | grep -i oom`);
- `upgrade.sql` впав (7.1) і причину не усунути швидко.

### 11.2 Відкат, якщо ще не дійшли до кроку 8.1 (старі контейнери лише зупинені)

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: запускає старі контейнери (повертає стару версію)]**
```bash
docker start shelfguard_api_core shelfguard_api_consumer shelfguard_api_provider shelfguard_web && for c in api_core api_consumer api_provider web; do sgwait shelfguard_$c || break; done
```
потім:
```bash
docker start shelfguard_gateway shelfguard_worker && sgwait shelfguard_gateway && sgwait shelfguard_worker
```
Часткова або повна нова схема БД ЗАЛИШАЄТЬСЯ: старий код з нею сумісний (розділ 12).

### 11.3 Відкат після кроку 8.1 (контейнери вже перестворені): назад на попередні образи за ID

Зупини вхід, поверни старі образи тим самим конфігом (rollback-специфікація зроблена з реальних контейнерів; образи: `sha256:c2991e01…` для API і `$OLD_WEB_ID` для web).

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: зупиняє gateway і worker (простій)]**
```bash
docker stop shelfguard_gateway shelfguard_worker
```

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: перестворює 4 контейнери зі СТАРИМИ образами]**
```bash
docker compose -p shelfguard --project-directory "$SG_RELEASE_DIR" -f "$SG_RELEASE_DIR/rollback.private.json" up -d --no-build --no-deps --force-recreate api_core api_consumer api_provider web > "$SG_RELEASE_DIR/rollback-compose.log" 2>&1; echo rc=$?
```

**[READ-ONLY]**
```bash
for c in api_core api_consumer api_provider web; do sgwait shelfguard_$c || break; done
```

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: відкриває трафік]**
```bash
docker start shelfguard_gateway shelfguard_worker && sgwait shelfguard_gateway && sgwait shelfguard_worker
```

**[READ-ONLY]** перевірка, що повернулись саме старі образи й конфіг:
```bash
python3 -I "$SG_RELEASE_DIR/tools/verify-containers.py" "$SG_RELEASE_DIR/before-private.json" "$OLD_API_ID" "$OLD_WEB_ID"; echo rc=$?
```
Очікується `rc=0`. Після відкату Beauty-ендпоінти знову `404` (стара версія): це очікувано. Далі повтори проби 3.6 і ручний вхід.

### 11.4 Коли НЕ відкочувати БД

За замовчуванням БД НЕ відкочується. Причини:
- Міграції адитивні (розділ 12): стара версія коду працює з новою схемою (нові колонки nullable/з default; `Message` розширено; нова таблиця старому коду не потрібна).
- Після відкриття трафіку в `landing_leads`/`beauty_tenant_links` вже можуть бути нові дані. `rollback.sql` видаляє колонки й таблицю (втрата даних), а звуження `Message` до 1000 символів ПАДАЄ або обрізає, якщо є довші значення (перевірка довжини в 3.5).
- Відновлення з дампу втрачає ВСІ бізнес-записи, зроблені після кроку 6.2.

Не відкочуй БД при: падінні/нездоровості контейнерів, 5xx, помилках перевірки env, збої в `upgrade.sql` (виправ і запусти знову: ідемпотентно).

### 11.5 Відкат БД (лише за рішенням людини, вручну)

Допустимо тільки якщо: БД пошкоджено/дані спотворено, і прийнято втрату всіх змін після простою, АБО потрібен точний повернення схеми й нових даних ще немає.

Варіант А (схема, коли нових даних немає або їх не шкода): `rollback.sql` (Down у зворотному порядку: RLS, потім control plane, потім criticality). Спершу зупини gateway/worker/web/api, потім:

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: НЕЗВОРОТНО видаляє нову таблицю й колонки разом з даними в них; тільки за рішенням людини після повного бекапу поточного стану]**
```bash
docker exec -i shelfguard_postgres psql -X -v ON_ERROR_STOP=1 -U shelfguard -d shelfguard < "$SG_RELEASE_DIR/rollback.sql" > "$SG_RELEASE_DIR/rollback.sql.apply.log" 2>&1; echo rc=$?
```
`rollback.sql` НЕ ідемпотентний (якщо міграцію вже відкотили, `DROP` дасть помилку). Він згенерований `dotnet ef migrations script 20261105090000_AddBeautyTenantLinksRls 20261102102841_AddReceiptCategoryWork` і перевірений локально (PostgreSQL 16, після upgrade → rollback історія повертається до `AddReceiptCategoryWork`, таблиці немає).

Варіант Б (повне відновлення з `database-quiesced-before.dump`): усі прод-контейнери застосунку зупинені; відновити в НОВУ БД поруч (напр. `shelfguard_restore`) через `pg_restore --exit-on-error -U shelfguard -d shelfguard_restore` (з власниками/привілеями, без `--no-owner`), звірити вручну, і лише тоді перейменувати БД (`ALTER DATABASE ... RENAME`, коли немає з'єднань). Ця процедура на проді НЕ репетирувалась (моя репетиція в етапі 4 робить restore з `--no-owner --no-privileges`), тому проведи її з людиною-DBA, поетапно, не командою «з одного рядка».

### 11.6 Forward-recovery (рекомендований шлях при проблемах застосунку)

Якщо схема застосована, а проблема в коді/конфігу застосунку: відкотити тільки контейнери (11.3), залишити БД, виправити код у `main`, дочекатися нового `release-package`, повторити розділи 5, 6 (без повторного повного дампу, якщо схема не змінилась, але зроби свіжий дамп), 8, 9 з новим артефактом. `upgrade.sql` перевипускати тільки якщо додались міграції (тоді знову `dotnet ef migrations script <остання-застосована> --idempotent`).

---

## 12. Чесна оцінка: чи міграції адитивні (за вмістом файлів)

Перевірено читанням `.cs` міграцій і згенерованого SQL.

1. `20261104155307_AddLandingLeadCriticality`: `ADD COLUMN CriticalReason varchar(50) NULL`, `ADD COLUMN IsCritical boolean NOT NULL DEFAULT false`. Адитивна. На PostgreSQL 11+ `ADD COLUMN ... DEFAULT <константа>` без перезапису таблиці.
2. `20261104223156_AddBeautyControlPlane`:
   - `ADD COLUMN` ×6 на `landing_leads` (усі nullable): адитивно;
   - `CREATE TABLE beauty_tenant_links` + 2 унікальні індекси; `CREATE UNIQUE INDEX IX_landing_leads_ExternalId ... WHERE "ExternalId" IS NOT NULL`: адитивно (нові рядки старим кодом не заповнюються, тож унікальність не конфліктує);
   - **НЕ суто адитивне, але безпечне: `ALTER COLUMN "Message" TYPE varchar(2000)`** (було 1000). Розширення довжини varchar: у PostgreSQL це зміна метаданих без перезапису таблиці й без втрати даних. Короткий `ACCESS EXCLUSIVE`-замок на `landing_leads`. Зворотне звуження в `rollback.sql` може впасти на довших значеннях.
   - `CREATE UNIQUE INDEX` (не `CONCURRENTLY`) блокує записи в `landing_leads` на час побудови; таблиця заявок невелика (кількість рядків дивись у 3.5), а сервіси на цей момент зупинені.
3. `20261105090000_AddBeautyTenantLinksRls`: SQL-only, `ENABLE`+`FORCE ROW LEVEL SECURITY` і 3 політики на НОВІЙ таблиці (`DROP POLICY IF EXISTS` стосується політик, яких іще не існує). Адитивна для існуючих даних. Поведінка: сесія без `app.tenant_id` і без ролі `provider`/`worker` бачить 0 рядків (за задумом; описано в коментарі міграції).

Є три `DROP POLICY IF EXISTS` в `upgrade.sql`; це єдині `DROP` у ньому, вони на щойно створеній таблиці. Вилучень колонок/таблиць у `upgrade.sql` немає.

Сумісність старого коду з новою схемою (висновок за аналізом міграцій, не тестований на проді): старий API ігнорує невідомі колонки; вставка в `landing_leads` без нових полів проходить (NULL / DEFAULT false); стара версія не звертається до `beauty_tenant_links`.

---

## 13. Чек-лист після релізу

### 13.1 Міграції й RLS (read-only, можна повторити в будь-який час)

**[READ-ONLY]**
```bash
sgsql -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" WHERE "MigrationId" >= $$20261102$$ ORDER BY 1'
```
Очікується чотири рядки: `20261102102841_AddReceiptCategoryWork`, `20261104155307_AddLandingLeadCriticality`, `20261104223156_AddBeautyControlPlane`, `20261105090000_AddBeautyTenantLinksRls`.

**[READ-ONLY]**
```bash
sgsql -c "SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname='beauty_tenant_links'" -c "SELECT policyname, cmd FROM pg_policies WHERE tablename='beauty_tenant_links' ORDER BY 1"
```
Очікується `beauty_tenant_links|t|t` і політики `provider_bypass`, `tenant_isolation`, `worker_bypass`.

**[READ-ONLY]** app-роль не обходить RLS:
```bash
sgsql -c "SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname='shelfguard_app'"
```
Очікується `t`.

**[READ-ONLY]** app-роль бачить 0 рядків без контексту tenant (fail-closed). Це вимагає під'єднання як `shelfguard_app` і секрету, тому НЕ виконується з цього runbook; покривається тестами CI. Достатньо `t|t` вище.

### 13.2 Запис релізу: ID образів і артефакту

**[READ-ONLY]** (несекретно; збережи вивід у запис релізу/репозиторій)
```bash
{ echo "release=$REL_TAG"; echo "mergeSha=$MERGE_SHA"; echo "payloadSha256=$PAYLOAD_SHA256"; echo "upgradeSqlSha256=$(sha256sum "$SG_RELEASE_DIR/upgrade.sql" | cut -d' ' -f1)"; echo "backupSha256=$(cut -d' ' -f1 "$SG_RELEASE_DIR/database-quiesced-before.dump.sha256")"; echo "oldApiImage=$OLD_API_ID"; echo "oldWebImage=$OLD_WEB_ID"; echo "newApiImage=$NEW_API_ID"; echo "newWebImage=$NEW_WEB_ID"; echo "downtime=$(cat "$SG_RELEASE_DIR/downtime-start.txt")..$(cat "$SG_RELEASE_DIR/downtime-end.txt")"; docker ps --filter name=shelfguard_ --format 'container={{.Names}} status={{.Status}}'; for c in api_core api_consumer api_provider web worker gateway; do docker inspect -f 'live {{.Name}} image={{.Image}}' shelfguard_$c; done; } | tee "$SG_RELEASE_DIR/release-record.txt"
```
Очікується, що live-образи `api_*` = `newApiImage`, `web` = `newWebImage`, `worker`/`gateway` без змін.

### 13.3 Прибирання

Період відкату: рекомендую 7 днів (збігається з retention артефакту). До кінця періоду НЕ запускати `docker image prune`.

**[READ-ONLY]** що є на диску:
```bash
ls -l "$SG_RELEASE_DIR" && du -sh "$SG_RELEASE_DIR" && docker images --format '{{.Repository}}:{{.Tag}} {{.ID}}' | grep -E 'shelfguard-(api|web):'
```

Після періоду відкату (лише коли реліз прийнято):

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: НЕЗВОРОТНО видаляє файли із секретами env і rollback-специфікації. Після цього відкат контейнерів за цим runbook неможливий]**
```bash
shred -u "$SG_RELEASE_DIR/before-private.json" "$SG_RELEASE_DIR/candidate.private.json" "$SG_RELEASE_DIR/rollback.private.json"
```

**[ПОПЕРЕДЖЕННЯ: ЗМІНЮЄ СТАН: НЕЗВОРОТНО видаляє серверну копію бекапу; перед цим переконайся, що копія поза сервером (6.3) цілісна; рекомендований мінімум зберігання бекапу 14 днів]**
```bash
shred -u "$SG_RELEASE_DIR/database-quiesced-before.dump"
```
Логи в каталозі релізу (`*.log`) не містять env, але зазирни перед архівуванням. Тимчасові репетиційні контейнери/мережі вже видалено в 4.5 (перевірка: `docker ps -a --filter name=sg-beauty-rehearsal` має бути порожньо).

---

## 14. Активація Beauty (окрема процедура, НЕ частина цього релізу)

Лише перелік змінних для API (значення не наводяться, беруться з менеджера секретів; кожна змінна задається в env контейнерів `api_core`, `api_consumer`, `api_provider`, що вимагає нової специфікації/перестворення контейнерів і є окремим human gate):
- `BeautyControl__Enabled` (true для ввімкнення);
- `BeautyControl__BaseUrl`, `BeautyControl__KeyId`, `BeautyControl__Secret` (напрямок ShelfGuard → Beauty, секрет base64 ≥ 32 байт);
- `BeautyFeedbackInbound__Keys__beauty-k1__Secret`, `BeautyFeedbackInbound__Keys__beauty-k1__Status` (напрямок Beauty → ShelfGuard, `active` або `retiring`).

Перед активацією: підтвердити, що `shelfguard_app` має доступ до `beauty_tenant_links` (7.3), узгодити ключі з Beauty-стороною, перевірити ввімкнення спочатку без реальних клієнтів. Gateway-конфіг для `/api/integrations/beauty/feedback` змін не потребує (маршрутизується на core за замовчуванням).
