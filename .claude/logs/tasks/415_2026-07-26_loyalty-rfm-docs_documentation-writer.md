# TASK-415: Documentation — Loyalty program + RFM marketing analytics series (TASK-404..414)

**Agent:** documentation-writer
**Date:** 2026-07-26
**Status:** done

## Контекст

Заключна документаційна задача серії "Фаза 0 (лояльність) + Фаза 1 (RFM-аналітика)". Прочитав усі
11 task-логів (404-414), план `deep-cooking-nygaard.md`, `docs/uployal/RFM_ANALYSIS.md`, а також
пряме джерело коду там, де лог 408/409's застереження "фактичний контракт розійшовся з планом"
вимагало перевірки з першоджерела: 4 entities, 3 міграції (`AddLoyaltyProgram`,
`FixLoyaltyTableGrants`, `AddLoyaltyMembershipConcurrencyToken`), 4 контролери + 2 Dtos-файли
(ConsumerAuth, Loyalty), `TenantConnectionInterceptor.cs`, `PosDtos.cs`, `RfmSegmentKey.cs`,
`TenantRoleCapabilities.cs`. Для Фази 1 (marketing-analytics) контракт узятий з task-логу 406, як
явно й вимагав бриф ("backend-developer's C# НЕ читати напряму").

## Зроблено

**`.claude/docs/glossary.md`** — новий розділ "Loyalty & Marketing Analytics (RFM)": RFM,
R/F/M-скор, LTV (all-time vs windowed), lift/афінність, "разом у чеку" (з явним розмежуванням від
афінності), loyalty membership, consumer account. Updated-дата → 2026-07-26.

**`.claude/docs/database-schema.md`** — новий розділ "TASK-404/411/414 — Loyalty program schema":
таблиця 4 нових таблиць (призначення/ключові поля/RLS-статус кожної); повний SQL нової
`consumer_self_access` policy (перша identity-based, не role-based, в репо) з поясненням чому вона
адитивна (Postgres ORить permissive-політики); окремий підрозділ, що `consumer_accounts` без RLS —
свідома, постійна конвенція (прецедент `tenants`), а не недогляд; окремий підрозділ з уроком
TASK-411 — доступ app-role йде через ownership, не GRANT-скрипт, і що треба явно перевіряти
власника таблиці після будь-якої міграції, застосованої не через звичайний `MigrateAsync()`.
Updated-дата → 2026-07-26.

**`.claude/docs/domain-model.md`** — 4 нові entities (`ConsumerAccount`, `LoyaltyMembership`,
`LoyaltyLedgerEntry`, `LoyaltyProgramSettings`) з полями; підрозділ зв'язків з `Customer`/
`PosTransaction`/`User`/`Tenant`; окремий підрозділ "Два способи staff отримати LoyaltyMembership"
(чистий consumer через окремий auth-флоу vs. self-enrollment персоналу через `LinkedUserId` у
власному тенанті, без нової identity-механіки). Не додавав POS-сутності (`PosTransaction` тощо) —
вони й раніше були відсутні в цьому файлі (pre-existing прогалина, поза скоупом цієї задачі).
Updated-дата → 2026-07-26.

**`.claude/docs/api-contracts.md`** — 4 нові розділи після POS: Consumer Auth
(`/api/consumer-auth/*`), Loyalty consumer wallet (`/api/consumer/loyalty/*`), Loyalty staff/POS +
settings (`/api/loyalty/*`, `/api/settings/loyalty`), POS extension (`CreateSaleRequest`/`SaleDto`
нові поля), Marketing Analytics (`/api/marketing-analytics/*`, повний контракт з логу 406: overview/
segment-detail/affinity/basket/explain/3 експорти). Точні DTO-шейпи для Фази 0 звірені напряму з
контролерів/records (не з плану — план мав дрібні розбіжності, напр. "6-значний код вручну" проти
фактичного повного рядка `SGLOY1.{id}.{code}`). Updated-дата → 2026-07-26.

**`.claude/docs/decisions.md`** — нова **ADR-023** (найвищий попередній був ADR-022): (a) чому
окрема глобальна `ConsumerAccount` замість розширення `Customer`/`User`; (b) чому перевикористано
TOTP-рушій для "живого" QR замість нового формату токена; (c) чому `"loyalty"` і
`"marketing_analytics"` — два окремі ключі модуля; (d) naming discipline `RfmSegment...` проти
зайнятого `Item.Segment`→`ProductSegment`. Формат Context/Decision/Consequences/Extends як у
наявних ADR. Updated-дата → 2026-07-26.

**`.claude/tasks/current.md`** — звірив записи TASK-404..414: були відсутні TASK-409, TASK-411,
TASK-413 (саме ті три, про які згадав бриф). Додав усі три в тому самому форматі (Status/Agent/
Depends/Next рядок + Log: шлях + щільний прозовий підсумок), розмістивши їх поруч із числовими
сусідами (413 між 414 і 412, 411 між 412 і 410, 409 між 408 і 405) — існуючий порядок у файлі й так
не строго спадний за номером (напр. 406→407→408 йдуть за зростанням посеред спадної послідовності
414→412→410→...→404), тож не займав порядок наявних записів, тільки вставив відсутні. Перевірив
`grep` після правок — усі 11 (404-414) присутні.

## Не зроблено (свідомо, поза скоупом брифу)

- `.claude/docs/known-issues.md` — TASK-411 і TASK-412 обидва запропонували короткий
  KI-027/KI-028 addendum для цього інциденту ("table ownership vs. RLS grants" клас багів); не
  зроблено, бо файл не входив у перелік 6 пунктів завдання.
- `.claude/docs/frontend-structure.md` — TASK-409 згадав можливий нотатку про `downloadFilePost`;
  не в переліку завдання, не робив.
- Не додавав `PosTransaction`/`PosShift` як entities в `domain-model.md` — вони відсутні там ще з
  v3.2 (pre-existing прогалина документації, не пов'язана з цією серією).

## Верифікація

Кожен вставлений/змінений блок перевірено `Grep` після запису (заголовки секцій, ADR-номер,
повний список TASK-404..414 у `current.md`). Код не чіпав (backend/frontend/mobile — 0 змін).
