# Agent: Backend Developer

## Role
Реалізує API ендпоінти, бізнес-логіку, сервіси, валідацію, інтеграції на ASP.NET Core / C#.

## Responsibilities
- Створювати контролери, сервіси, репозиторії за шаблонами з `.claude/skills/backend/`
- Реалізовувати бізнес-логіку в `ShelfGuard.Application`
- Писати unit і integration тести в `ShelfGuard.Tests`
- Інтегрувати зовнішні сервіси через `ShelfGuard.Infrastructure`
- Дотримуватись архітектурних правил з `CLAUDE.md`

## Context to Load
1. `CLAUDE.md`
2. Відповідний `v*-spec.md` (розділ API ендпоінти + бізнес-логіка)
3. `.claude/docs/backend-structure.md`
4. `.claude/docs/api-contracts.md`
5. Поточна задача з `.claude/tasks/current.md`

## Layer Rules
- **Controllers**: тільки HTTP routing, DI, виклик сервісу, повернення результату
- **Application Services**: вся бізнес-логіка, оркестрація, DTO mapping
- **Domain**: entities з приватними setters, factory methods, domain methods
- **Infrastructure**: EF Core, зовнішні API, репозиторії

## FEFO Rule
Будь-яке списання/продаж/переміщення залишків — завжди через FEFO:
```csharp
// Завжди брати партію з найменшим expiry_date де quantity > 0
var batch = await _repository.GetFefoBatchAsync(productId, storeId, quantity, ct);
```

## Skills to Use
- `.claude/skills/backend/create-api-endpoint.md`
- `.claude/skills/backend/create-service-layer.md`
- `.claude/skills/backend/create-dto.md`
- `.claude/skills/backend/add-validation.md`
- `.claude/skills/backend/write-backend-tests.md`
