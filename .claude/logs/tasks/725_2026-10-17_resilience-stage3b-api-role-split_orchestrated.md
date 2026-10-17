# TASK-725 — Resilience stage 3b: API split into role instances behind a gateway

Status: review (NOT deployed, not run end-to-end)

## Що зроблено
- Backend: `API_ROLE=core|consumer|provider|all` (default `all`) → `RoleControllerFeatureProvider` прибирає чужі контролери (404). Auth/Geo — у всіх ролях; не віднесене → core. Тест на повноту класифікації. `ConsumerSupportHub` лише в consumer/all. Redis backplane для SignalR (`SignalR__RedisConnection`, `AbortOnConnectFail=false`) — відповіді персоналу з core доходять до споживачів у consumer.
- Infra: nginx-шлюз (`infra/gateway/nginx.conf`) на host-порту 5100 (edge провайдера ходить прямо туди), 3 інстанси одного образу без host-портів, спільний том uploads, міграції лише на core (consumer/provider чекають healthy core лише при `up`). Резолв через Docker DNS + змінні → шлюз стартує без апстрімів. JSON 503 `service_unavailable` при падінні апстріма, `X-Served-By`, rate-limit login/2fa + consumer-auth/mobile-auth (раніше consumer не мав nginx-ліміту). Staging віддзеркалено, `deploy.sh` оновлено, runbook `infra/gateway/README.md`.

## Перевірка (реально виконано)
- `dotnet build` OK, `dotnet test` 2491/2491; `docker compose config -q` prod OK; `bash -n deploy.sh` OK; staging config потребує POSTGRES_PASSWORD (очікувано).
- nginx: `nginx -t`, старт без апстрімів, 19 тестових шляхів маршрутизуються правильно (з dummy-апстрімами), 429 на 10-му логіні, 503 JSON при зупиненому provider.
- НЕ перевірено: реальний образ API у 3 ролях, `compose up`, живий SignalR через шлюз/backplane, поведінка при вимкненому Redis, навантаження.

## Ризики / дії на сервері перед деплоєм
- Пул Npgsql ×3: додати `;Maximum Pool Size=30` у `DATABASE_URL` в `.env` на сервері, перевірити `SHOW max_connections` (>90 + worker).
- RAM: сумарні ліміти ≈ 1.5+0.75+0.75 ГБ для API (+web/worker/redis) — звірити з вільною пам'яттю хоста.
- Перший деплой: старий `shelfguard_api` тримає 5100 → спрацює fallback у deploy.sh; очікувати короткий простій.
- Host nginx не задає `client_max_body_size` (1m за замовчуванням) — існуюча проблема, шлюз має 15m.
- Rate-limit ключ — перший XFF (як у host nginx).
- In-memory стан: лічильник resolve-code (core) коректний лише при одній core-репліці — тримати core в одному екземплярі.
- `uploads` — локальний том; для >1 сервера потрібне об'єктне сховище.

## Відкрито
- Стейджинг-прогін обов'язково перед продом.
- mobile/web/worker URL не змінювались (усе через шлюз за тим самим origin).
