# Доступ працівника до приймання marketplace-замовлень

Дата аналізу: 2026-08-22

## Підсумок

Сам сценарій приймання товару вже реалізований. Проблема знаходиться раніше в потоці: у виборі
між покупецькою (`/(personal)`) та робочою (`/(app)`) зонами застосунку і, додатково, у перевірках
доступу до модуля marketplace.

Конструктор блоків не додає і не приховує робоче приймання. Опублікований mobile config
застосовується тільки до покупецької зони (`mobile/app/(personal)`): її сторінок, блоків, теми та
нижньої навігації. Робоча зона працівника має окрему статичну навігацію в
`mobile/app/(app)/_layout.tsx` та `mobile/app/(app)/more/index.tsx`.

## Підтверджена основна причина

`useMobileLogin()` після будь-якого успішного входу виконує:

```ts
router.replace('/(personal)');
```

Це відбувається навіть тоді, коли `POST /api/mobile-auth/login` повернув
`workspaceAccessToken`, `access.canAccessWorkspace: true` і роль `store_manager`. Токен і профіль
працівника зберігаються правильно, але користувач залишається в покупецькій оболонці. У
покупецькій оболонці немає кнопки переходу до `/(app)`, тому з інтерфейсу користувач не може
дістатися робочих функцій.

Та сама поведінка присутня в `useConsumerAuth()` для реєстрації/входу через consumer flow.

### Необхідна mobile-зміна

Після збереження обох незалежних сесій маршрут треба вибирати з відповіді backend:

```ts
const hasWorkspace =
  result.access.canAccessWorkspace && Boolean(result.workspaceAccessToken);

router.replace(hasWorkspace ? '/(app)' : '/(personal)');
```

Якщо продуктово працівник повинен спочатку потрапляти в покупецьку зону, замість автоматичного
переходу необхідно додати видимий перемикач «Робочий простір / Особистий кабінет» в обох
оболонках. Наявність робочого режиму слід визначати за `workspaceAccessToken`, а не за
`personalAccessToken` і не за опублікованими блоками.

Також потрібні тести:

1. linked staff, обидва токени -> перехід у `/(app)`;
2. legacy staff, лише workspace token -> перехід у `/(app)`;
3. звичайний покупець, лише personal token -> перехід у `/(personal)`;
4. linked staff із 2FA -> після підтвердження 2FA перехід у `/(app)`;
5. працівник може вручну перемикатися між зонами, якщо буде обрано варіант із перемикачем.

## Що backend уже передає

Поточний `MobileLoginResponse` уже містить достатньо даних для правильного вибору оболонки:

```json
{
  "personalAccessToken": "... або null",
  "workspaceAccessToken": "... або null",
  "user": {
    "id": "guid",
    "tenantId": "guid або null",
    "storeId": "guid або null"
  },
  "access": {
    "canAccessWorkspace": true,
    "role": "store_manager",
    "permissions": {},
    "capabilities": [],
    "tabs": ["marketplace"]
  }
}
```

Тому розширювати DTO лише заради відображення приймання не потрібно. Backend-завдання полягає
в перевірці коректності даних конкретного користувача та tenant-конфігурації.

## Серверні умови доступу до приймання

Навіть після переходу в `/(app)` пункт «Приймання замовлень» відображається лише коли одночасно
виконані всі умови:

1. Є `workspaceAccessToken`, а `access.canAccessWorkspace` дорівнює `true`.
2. `access.role` дорівнює `storekeeper`, `store_manager`, `network_manager` або
   `enterprise_admin`.
3. `GET /api/settings/modules` повертає `modules`, що містить `marketplace`.
4. Якщо `access.tabs` не порожній, він має містити `marketplace`.
5. Для мутацій JWT має задовольняти backend policy `CanReceiveStock`.

Особливість пункту 4: `navigationDecision()` спочатку дозволяє роль менеджера, але потім окремо
забороняє маршрут, якщо список `tabs` непорожній і в ньому немає `marketplace`. Отже кастомна
tenant-role без дозволеної вкладки `marketplace` приховає приймання навіть для
`store_manager`.

## Що перевірити на backend для проблемного менеджера

Для відповіді `POST /api/mobile-auth/login` перевірити:

- знайдений `ConsumerAccount` справді зв'язаний з активним staff `User`;
- `workspaceAccessToken` не `null`;
- `access.canAccessWorkspace == true`;
- `access.role == "store_manager"`;
- `user.tenantId` і `user.storeId` не `null` та належать потрібному магазину;
- `access.tabs` порожній (legacy/default mode) або містить `marketplace`;
- якщо використовується `TenantRole`, вона активна, а її `AllowedTabs` містить `marketplace`.

Для `GET /api/settings/modules` перевірити, що відповідь для tenant менеджера містить:

```json
{
  "businessType": "...",
  "modules": ["marketplace"]
}
```

Список може містити й інші модулі. Важлива наявність `marketplace`.

Якщо `workspaceAccessToken == null`, це не проблема marketplace API. Це означає, що
`MobileAuthController` не знайшов активного staff-користувача, зв'язаного з consumer-акаунтом.
Потрібно виправити зв'язок акаунтів/ідентифікаторів або процес створення такого зв'язку.

## Рекомендоване діагностичне розширення backend

Обов'язкових нових полів у login response немає. Для спрощення підтримки можна додати окремий
авторизований endpoint на кшталт `GET /api/mobile-auth/context`, який повертає вже обчислений
контекст без токенів:

```json
{
  "mode": "linked_staff",
  "canAccessWorkspace": true,
  "role": "store_manager",
  "tenantId": "guid",
  "storeId": "guid",
  "modules": ["inventory", "procurement", "marketplace"],
  "tabs": ["marketplace"],
  "capabilities": [],
  "workspaceFeatures": {
    "marketplaceOrderReceiving": true
  }
}
```

`workspaceFeatures.marketplaceOrderReceiving` має бути похідним серверним значенням, а не ще
одним незалежним прапорцем у БД. Воно повинно обчислюватися з активного модуля, ролі/tab та
policy. Це розширення корисне для діагностики й server-driven navigation, але не є блокером для
виправлення поточного mobile flow.

## Межа відповідальності конструктора блоків

Поточний app builder редагує consumer-facing `MobileConfig` і не повинен випадково визначати
права працівників. Є два коректні подальші варіанти:

- залишити робочу навігацію статичною та role/module/tab-gated, як зараз;
- створити окремий `workspaceNavigation`/`workspacePages` контракт для server-driven UI
  працівників.

Не слід додавати маршрут `/(app)/marketplace-orders` у поточний consumer `navigation`: він
завантажується через personal API client, доступний покупцям і має іншу модель авторизації.
Якщо потрібне редагування робочої оболонки з web preview, це має бути окремий документ із
дозволеним реєстром workspace-маршрутів. Backend при публікації мусить валідувати, що tenant не
може опублікувати маршрут, якого немає в його modules, а mobile все одно мусить повторно
застосувати role/module/tab guard.

## Критерії готовності

Після виправлення менеджер магазину з активним marketplace-модулем:

1. входить через `/api/mobile-auth/login` і отримує workspace session;
2. потрапляє в `/(app)` або бачить явний перемикач робочого режиму;
3. у вкладці «Ще» бачить «Приймання замовлень»;
4. відкриває `/(app)/marketplace-orders` без `GuardState`;
5. бачить список із `GET /api/marketplace/orders/awaiting-receipt`;
6. може почати, заповнити та фіналізувати приймання;
7. звичайний покупець без workspace session не бачить і не може відкрити цей розділ.
