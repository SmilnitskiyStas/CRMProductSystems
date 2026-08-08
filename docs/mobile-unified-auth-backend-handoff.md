# Handoff: єдина мобільна авторизація та зв’язування працівника

## Мета

Мобільний застосунок Expo 56 повинен мати один onboarding та одну форму входу для всіх користувачів.

Користувач не вибирає роль «клієнт» або «працівник». І звичайний користувач, і працівник можуть входити через email або український номер телефону. Backend сам визначає наявність робочого доступу.

Після входу всі користувачі відкривають особистий режим. Якщо особистий акаунт зв’язаний з активним записом працівника `users`, застосунок показує кнопку переходу до робочого простору.

## Узгоджена модель

- `consumer_accounts` — основна особиста мобільна ідентичність.
- `users` — обліковий запис працівника з роллю, tenant, permissions, capabilities та tabs.
- Поточне автоматичне зв’язування виконується за однаковим нормалізованим телефоном або email.
- Роль ніколи не передається користувачем під час реєстрації.
- `canAccessWorkspace` і всі робочі права визначає тільки backend.
- Якщо `ConsumerAccount` для ідентифікатора існує, його пароль є основним мобільним паролем.
- Staff-auth використовується як fallback для запрошених працівників, які ще не створили особистий `ConsumerAccount`.

## Уже внесені зміни

### Backend

Доданий єдиний контролер:

- `backend/ShelfGuard.Api/Controllers/MobileAuthController.cs`

Endpoints:

```http
POST /api/mobile-auth/register
POST /api/mobile-auth/login
```

Доданий контракт відповіді:

- `backend/ShelfGuard.Application/Features/MobileAuth/Dtos/MobileAuthDtos.cs`

Приклад відповіді для звичайного користувача:

```json
{
  "accessToken": "consumer-jwt",
  "user": {
    "id": "consumer-account-id",
    "fullName": "Іван Іваненко",
    "email": "user@example.com",
    "phone": "+380501234567",
    "tenantId": null,
    "storeId": null
  },
  "access": {
    "canAccessWorkspace": false,
    "role": "consumer",
    "permissions": {},
    "capabilities": [],
    "tabs": []
  }
}
```

Приклад відповіді для прив’язаного працівника:

```json
{
  "accessToken": "staff-jwt",
  "user": {
    "id": "user-id",
    "fullName": "Іван Іваненко",
    "email": "worker@example.com",
    "phone": null,
    "tenantId": "tenant-id",
    "storeId": "store-id"
  },
  "access": {
    "canAccessWorkspace": true,
    "role": "staff",
    "permissions": {},
    "capabilities": [],
    "tabs": []
  }
}
```

Змінені repository-контракти:

- `IUserRepository.GetByPhoneAsync`
- `IConsumerAccountRepository.GetByEmailAsync`

Змінені реалізації:

- `backend/ShelfGuard.Infrastructure/Data/Repositories/UserRepository.cs`
- `backend/ShelfGuard.Infrastructure/Data/Repositories/ConsumerAccountRepository.cs`

До `IAuthService` та `AuthService` додано `IssueLinkedMobileSessionAsync`. Метод видає робочу сесію тільки для активного `User` і зберігає перевірку staff 2FA.

У `ConsumerAuthService`:

- email нового акаунта приводиться до lowercase;
- перевіряється дублювання consumer email;
- дублювання телефону вже перевірялося раніше.

У `UserService`:

- телефон працівника нормалізується до `+380XXXXXXXXX`;
- невалідний український номер відхиляється;
- один номер заборонено призначати двом працівникам на application-рівні.

### Mobile Expo 56

Onboarding більше не містить вибору ролі:

- `mobile/app/(auth)/select-role.tsx`

Єдина форма входу приймає email або телефон:

- `mobile/app/(auth)/consumer-login.tsx`
- `mobile/features/auth/api/mobileAuthApi.ts`
- `mobile/features/auth/hooks/useMobileLogin.ts`

Реєстрація також переведена на `/api/mobile-auth/register`, щоб прив’язаний працівник отримував робочий доступ одразу, без повторного входу:

- `mobile/features/auth/hooks/useConsumerAuth.ts`

Доданий спільний особистий режим:

- `mobile/app/(personal)/_layout.tsx`
- `mobile/app/(personal)/index.tsx`
- `mobile/app/(personal)/account.tsx`

Після входу всі користувачі відкривають `/(personal)`. Кнопка «Робочий простір» відображається тільки для staff-сесії. У робочому dashboard додано повернення в особистий режим.

## Поточний алгоритм входу

1. Нормалізувати введений identifier.
2. Знайти `ConsumerAccount` за email або телефоном.
3. Якщо `ConsumerAccount` знайдений — перевірити його пароль. Не виконувати fallback на staff-пароль при неправильному consumer-паролі, щоб не створювати подвійні невдалі спроби та lockout.
4. Після успішної consumer-авторизації знайти активний `User`:
   - спочатку за `consumer_accounts.phone = users.phone`;
   - потім за case-insensitive email.
5. Якщо `User` знайдений — видати staff JWT і повернути `canAccessWorkspace: true`.
6. Якщо зв’язку немає — видати consumer JWT і повернути `canAccessWorkspace: false`.
7. Якщо `ConsumerAccount` не існує — знайти `User` за email або телефоном і виконати старий staff-auth як backward-compatible fallback.

## Завдання для backend/database-агента

### 1. Перевірити та завершити модель зв’язування

Поточне зіставлення за контактами працездатне, але довгостроково рекомендовано додати явний nullable FK:

```text
consumer_accounts.linked_user_id -> users.id
```

Рекомендована поведінка:

- спочатку використовувати `linked_user_id`;
- зіставлення за телефоном/email використовувати для первинного автоматичного зв’язування;
- після однозначного збігу записувати `linked_user_id`;
- не зв’язувати акаунт автоматично, якщо знайдено більше одного кандидата;
- при деактивації `User` залишати особистий акаунт активним, але повертати `canAccessWorkspace: false`.

### 2. Підготувати міграцію даних

Перед унікальними індексами перевірити дублікати:

```sql
SELECT phone, COUNT(*)
FROM users
WHERE phone IS NOT NULL AND phone <> ''
GROUP BY phone
HAVING COUNT(*) > 1;

SELECT LOWER(email), COUNT(*)
FROM consumer_accounts
WHERE email IS NOT NULL AND email <> ''
GROUP BY LOWER(email)
HAVING COUNT(*) > 1;
```

Перевірити потенційні зв’язки:

```sql
SELECT
    u.id AS user_id,
    ca.id AS consumer_account_id,
    u.email AS user_email,
    ca.email AS consumer_email,
    u.phone AS user_phone,
    ca.phone AS consumer_phone
FROM users u
JOIN consumer_accounts ca
  ON u.phone = ca.phone
  OR LOWER(u.email) = LOWER(ca.email);
```

Не виконувати автоматичний destructive merge дублікатів. Сформувати звіт і погодити конфліктні записи окремо.

### 3. Нормалізувати дані

- `users.phone` і `consumer_accounts.phone`: `+380XXXXXXXXX`.
- Email: trim + lowercase.
- Перед масовим update зробити backup або перевірену reversible migration.
- Невалідні телефони винести у звіт, не перетворювати приблизно.

### 4. Додати database-обмеження

Після очищення даних рекомендовано:

- partial unique index для непорожнього `users.phone`;
- case-insensitive unique index для непорожнього `consumer_accounts.email`;
- unique index для `consumer_accounts.linked_user_id`, якщо зв’язок 1:1;
- FK з поведінкою `ON DELETE SET NULL` або еквівалентом EF Core;
- міграцію та оновлення `AppDbContextModelSnapshot` створити стандартним EF Core workflow.

### 5. Перевірити токени

Поточна реалізація для прив’язаного працівника повертає staff JWT. Перед реалізацією повного personal-функціоналу потрібно остаточно вирішити один із варіантів:

1. Один комбінований JWT, який містить і staff claims, і `consumer_account_id`.
2. Окремі personal/workspace токени з безпечним перемиканням у mobile store.
3. Розширити personal API так, щоб staff JWT міг отримувати особисті дані прив’язаного `ConsumerAccount`.

Необхідно обрати один варіант до підключення бонусного гаманця та історії покупок у новий `/(personal)` режим. Поточний personal-екран не викликає consumer loyalty API, тому наявна реалізація працює для поточного UI.

## Security-вимоги

- Не довіряти `role`, `canAccessWorkspace`, permissions або user ID із mobile request.
- Робочі endpoints продовжують перевіряти JWT і permissions на backend.
- При зв’язуванні враховувати тільки активний `User`.
- Не обходити staff 2FA. Поточний `IssueLinkedMobileSessionAsync` повертає 2FA challenge для staff з активним TOTP.
- Не виконувати staff fallback, якщо `ConsumerAccount` для identifier існує, але його пароль неправильний.
- Не повертати різні auth-помилки, які дозволяють визначити існування consumer/staff акаунта.
- Зберегти rate limiting для mobile login/register.

## Тести, які вже додані

- `backend/ShelfGuard.Tests/Auth/MobileAuthControllerTests.cs`
- `backend/ShelfGuard.Tests/Auth/MobileLoginResponseFactoryTests.cs`
- `mobile/features/auth/api/__tests__/mobileAuthApi.test.ts`

Покриті сценарії:

- consumer login телефоном;
- consumer login email;
- автоматичне визначення прив’язаного працівника;
- негайний workspace access після реєстрації за прив’язаним телефоном;
- staff fallback через телефон;
- звичайний consumer без workspace access;
- дублювання consumer email;
- 2FA challenge у mobile auth-контракті.

Останні локальні результати:

- mobile TypeScript: успішно;
- mobile auth tests: 22/22;
- backend mobile/auth/user test subset: 80/80;
- попередній розширений backend auth/user subset: 100/100;
- backend build: успішно;
- існує старе, не пов’язане попередження nullable у `MarketplaceServiceTests.cs`.

## Критерії приймання

1. Звичайний користувач може зареєструватися та входити через телефон.
2. Якщо в consumer-профілі збережено email, він може входити також через email.
3. Працівник може входити через email або прив’язаний телефон.
4. Реєстрація consumer-акаунта з телефоном/email активного працівника одразу повертає `canAccessWorkspace: true`.
5. Неприв’язаний consumer отримує `canAccessWorkspace: false`.
6. Деактивований працівник не отримує робочий доступ.
7. Звичайний користувач не може відкрити робочі API прямим запитом.
8. Permissions, capabilities та tabs відповідають поточним серверним правилам працівника.
9. 2FA працівника залишається обов’язковою.
10. Після перезапуску застосунку сесія відновлюється у personal-режимі, а робоча кнопка зберігається лише для працівника.

## Важливо для агента

Робоче дерево вже містило багато сторонніх змін до початку цієї задачі. Не робити `git reset`, не перезаписувати чужі файли та перед комітом відокремити зміни цієї задачі за наведеним переліком.
