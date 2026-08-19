# Mobile Application — Current State Audit

Дата аудиту: 2026-08-17  
Область: `mobile/`  
Цільова специфікація: `docs/CODEX SPEC — Mobile Application.md`

## 1. Executive summary

Мобільний застосунок уже є зрілим Expo/React Native клієнтом, а не порожнім стартовим
проєктом. Він містить дві незалежні користувацькі оболонки:

- `/(personal)` — особистий режим покупця: програми лояльності, каталог, акції,
  новини, гаманець та історія;
- `/(app)` — робочий простір працівника: склад, POS, приймання, переміщення,
  списання та інші операційні модулі.

Новий Multi-Tenant Server-Driven Retail Engine потрібно будувати поверх personal-режиму,
зберігаючи workspace як окрему RBAC-захищену оболонку. Найважливіша архітектурна зміна —
створити єдиний глобальний контекст активного retailer і підпорядкувати йому personal UI,
mobile configuration, theme, feature flags, navigation та всі tenant-scoped запити.

Поточна feature-based структура придатна для розвитку. Масове перенесення проєкту до
нового `src/` не потрібне і створить зайвий ризик регресій.

## 2. Technology and project setup

Поточний стек:

- Expo SDK 56, React Native 0.85, React 19;
- Expo Router із file-based routing;
- TypeScript у strict mode;
- React Query 5 для server state;
- Zustand 5 для auth та локального UI state;
- Axios для HTTP;
- Zod для form/input validation;
- AJV встановлений, але canonical mobile-config schema ще не інтегрований;
- Secure Store для токенів та identity snapshots;
- AsyncStorage для несекретного persisted state, offline cache та operational drafts;
- NativeWind/Tailwind для стилів;
- Jest + jest-expo + Testing Library;
- Expo Updates, EAS, camera, location, notifications, QR та Code 128.

Корисні scripts у `mobile/package.json`:

```text
npm run type-check
npm run lint
npm run test:ci
npm run android
npm run web
```

На момент аудиту знайдено приблизно 53 route/screen TSX-файли, 21 feature directory та
32 test-файли.

## 3. Current directory architecture

```text
mobile/
├── app/                   Expo Router routes
│   ├── (auth)/            onboarding, login, registration, 2FA
│   ├── (personal)/        customer/consumer experience
│   └── (app)/             staff workspace
├── features/              feature-owned API, hooks, types, stores, components
├── components/            shared UI components
├── lib/                   API clients, query client, roles and shared utilities
├── plugins/               Expo config plugins
├── android/               generated/native Android project
└── app.json               Expo application configuration
```

Ця структура вже відповідає правилу feature ownership. Для нової архітектури доцільно
додавати нові top-level feature modules без перенесення наявного коду:

```text
features/
├── tenant/
├── mobile-config/
├── theme/
├── feature-flags/
└── server-driven-ui/
```

Shared renderer primitives за потреби можуть бути розміщені в `components/blocks/`, але
API, validation, caching та domain types повинні залишатися у відповідних feature modules.

## 4. Application bootstrap and providers

Root layout `mobile/app/_layout.tsx` зараз:

- запускає `bootstrapSession()`;
- створює `QueryClientProvider`;
- створює `SafeAreaProvider`;
- реєструє `(auth)`, `(personal)` та `(app)` stacks.

Відсутні потрібні новою специфікацією providers:

- `ActiveTenantProvider`;
- `MobileConfigProvider`;
- dynamic `ThemeProvider`;
- `FeatureFlagProvider`.

Ці providers не слід додавати всі безумовно на найвищий root-рівень. Tenant config має
завантажуватися лише після успішного auth bootstrap і визначення доступного active tenant.
Workspace не повинен залежати від customer mobile config.

Рекомендована межа — окремий personal retail shell усередині `/(personal)`.

## 5. Authentication and session model

Уже реалізована unified mobile authentication із двома незалежними identities:

- `personalAccessToken` — ConsumerAccount/loyalty API;
- `workspaceAccessToken` — staff/tenant API;
- обидва токени можуть існувати одночасно;
- токени зберігаються в Secure Store;
- персональний та робочий API clients фізично розділені;
- staff flow має refresh-and-retry та terminal session cleanup;
- 2FA залишається обов'язковою для staff;
- bootstrap підтримує retryable error та обмежений offline-read mode.

Основні файли:

- `features/auth/store.ts`;
- `features/auth/bootstrap.ts`;
- `features/auth/session.ts`;
- `features/auth/api/mobileAuthApi.ts`;
- `lib/api-client.ts`.

Сильна сторона: personal JWT структурно не використовується workspace features, оскільки
consumer API імпортує `personalApiClient`, а операційні features — `apiClient`.

Відкрита залежність від backend: потрібно остаточно зафіксувати довгострокову модель
зв'язування `ConsumerAccount` і staff `User`, а також правила refresh/expiry personal token.
Це не блокує mock-based Stage 1, але блокує фінальний production bootstrap нового retail shell.

## 6. Navigation

### Personal navigation

`mobile/app/(personal)/_layout.tsx` містить hardcoded tabs:

- home;
- catalog;
- wallet;
- history;
- account.

Wallet/history приховуються без personal token. Кольори, labels, icons та порядок зараз
hardcoded. Саме цей layout є основною точкою заміни на whitelist-based navigation config.

### Workspace navigation

`mobile/app/(app)/_layout.tsx` містить окрему складну navigation policy:

- role/capability/tab guards;
- module activation;
- business type restrictions;
- offline route allowlist;
- hidden operational routes.

Цю систему не слід замінювати customer feature flags. Workspace navigation та retailer
customer navigation мають різні security semantics і повинні залишатися окремими.

## 7. Tenant state and current multi-retailer UX

Часткова multi-retailer підтримка вже існує у loyalty feature:

- memberships завантажуються з backend;
- користувач може приєднуватися до retailer/network;
- `useLoyaltyUiStore.selectedTenantId` спільний між home, wallet, history, catalog,
  product та news screens;
- якщо selection відсутній або membership видалено, перша доступна membership обирається
  автоматично;
- tenant і preferred store передаються до consumer content API.

Обмеження:

- `selectedTenantId` не persisted між перезапусками;
- це loyalty-specific UI state, а не application-wide tenant context;
- selection logic частково дублюється між screens;
- tenant switch не має централізованої cache invalidation/cancellation policy;
- немає єдиного lifecycle `restore tenant -> validate membership -> load config`;
- немає станів deleted/disabled tenant на рівні application shell.

На Stage 1/2 потрібно створити єдиний `activeTenantId`. Поточний loyalty selection слід
мігрувати на нього або зробити тонким adapter, але не залишати два незалежні джерела істини.

## 8. API architecture

`mobile/lib/api-client.ts` створює два Axios clients із base URL:

```text
EXPO_PUBLIC_API_URL ?? http://localhost:5000/api
```

- `apiClient` — workspace token, refresh flow, session termination;
- `personalApiClient` — personal token, без refresh flow.

API modules переважно feature-owned і використовують React Query hooks. Це потрібно
повторно використати для tenant discovery та mobile config.

Поточні consumer endpoints використовують explicit tenant ID у URL або query parameters.
Нова специфікація описує `/api/v1/mobile/config`, де tenant визначається authenticated user
та active tenant, але спосіб передачі active tenant ще не визначений.

До реалізації production API потрібні:

- canonical OpenAPI contract;
- `contracts/mobile-config.schema.json`;
- узгоджений tenant context transport;
- versioning policy для поточних unversioned `/api/...` endpoints і нових `/api/v1/...`;
- стандартизований error contract для invalid/removed tenant і config errors.

Mobile не повинен вигадувати ці backend рішення. До готовності контракту Stage 1 має
використовувати typed mock adapter/repository.

## 9. State management and persistence

Поточний поділ загалом коректний:

- React Query володіє server state;
- Zustand використовується для auth та UI selection;
- Secure Store зберігає secrets та identity snapshots;
- AsyncStorage використовується для несекретного persistent state.

Існує добре ізольований offline read-cache для окремих workspace query families. Він має:

- owner namespace із tenant/user;
- schema version;
- soft/hard TTL;
- size limits;
- field-level sanitization;
- fail-closed parsing;
- очищення при logout/account switch.

Його принципи можна повторно використати для mobile config cache, але сам allowlist cache
не слід напряму розширювати customer config без окремої policy. Last Valid Configuration
потребує власної версії, tenant namespace, runtime validation та atomic replacement.

## 10. Existing reusable customer features

У personal experience уже є значний reusable функціонал:

- retailer memberships і network discovery;
- loyalty balance/history;
- rotating QR/Code128 customer code;
- preferred store;
- banners and promotions;
- product catalog and details;
- news details;
- shopping favorites/cart UI state;
- location-based nearest store flow;
- workspace entry для linked staff.

Ці screens зараз переважно великі й layout-driven без block abstraction. Їх domain hooks,
API та leaf components можна повторно використати, але home/catalog UI потрібно поступово
розкласти на typed blocks/data providers для `PageRenderer`.

## 11. Theme and UI system

Поточний UI використовує NativeWind, спільні UI components та багато hardcoded green/gray
кольорів у layouts і screens. Dynamic theme engine відсутній.

Потрібний refactor має бути token-first:

- обмежений `ThemeConfig`;
- validated color/radius/spacing values;
- safe default theme;
- hook/components читають semantic tokens;
- server не може передавати arbitrary class names, styles або CSS.

Не потрібно одномоментно переписувати workspace UI. Першою межею dynamic theme має бути
новий personal retail shell та server-driven blocks.

## 12. Server-driven configuration readiness

На момент аудиту відсутні:

- canonical `mobile-config.schema.json`;
- `MobileConfig` runtime validator;
- config API/repository;
- last-valid config cache;
- config version compatibility policy;
- component registry;
- block/page renderer;
- unknown block telemetry/warning abstraction;
- dynamic customer navigation builder;
- preview mode.

AJV уже встановлений, тому JSON Schema може бути canonical runtime boundary без додаткової
dependency. TypeScript types не повинні вручну розходитися зі schema; після появи contract
потрібно або генерувати types, або додати contract conformance tests.

## 13. Feature flags

Workspace уже має backend module activation і route policy. Це не еквівалент customer
feature flags із `MobileConfig`.

Для personal engine потрібна окрема централізована abstraction, наприклад
`useRetailFeature(key)`, яка одночасно впливає на:

- navigation visibility;
- block visibility;
- route guards;
- data-fetch enablement.

Feature check не повинен дублюватися хаотично по screens. Водночас mobile flags є лише UX
guard; backend все одно зобов'язаний забороняти disabled feature operations.

## 14. Tests and quality gates

Наявний Jest setup покриває auth, API client lifecycle, navigation policy, loyalty API,
offline cache, operational drafts, POS policy/submission та окремі UI components.

Stage 1+ може повторно використати:

- Jest + Testing Library;
- axios-mock-adapter;
- тестові patterns для Zustand stores;
- pure policy function tests;
- boundary parsing tests.

Обов'язкове нове покриття:

- active tenant persistence and invalidation;
- config schema validation;
- last-valid fallback;
- theme token generation;
- feature flag policy;
- component registry and unknown blocks;
- Tenant A -> switch -> Tenant B query/cache isolation;
- loyalty/config/content data A ніколи не з'являються в UI B.

Quality gates для кожного наступного stage:

```text
npm run type-check
npm run lint
npm run test:ci
npx expo export --platform android (коли stage зачіпає runtime/build integration)
```

## 15. Conflicts and risks

### High priority

1. Немає canonical config schema/API contract.
2. `selectedTenantId` не є persisted global tenant context.
3. Tenant switch поки не гарантує централізоване cancel/remove tenant-scoped queries.
4. Personal token не має refresh/me lifecycle, що впливає на надійний cold bootstrap.
5. У робочому дереві вже є багато незакомічених mobile changes; наступні stages мають
   торкатися вузького переліку файлів і не перезаписувати сторонню роботу.

### Medium priority

1. Personal tabs і theme повністю hardcoded.
2. Великі personal screens потребують поступової декомпозиції на blocks.
3. API versioning у поточному коді не відповідає новому `/api/v1/` contract.
4. Немає централізованої telemetry abstraction для config/block failures.
5. Немає preview/deep-link config flow.

## 16. Reuse / refactor / create decisions

### Reuse as-is or with small extension

- Expo Router shells;
- auth store/bootstrap/session boundaries;
- separate personal/workspace API clients;
- React Query and query-key patterns;
- Secure Store/AsyncStorage choices;
- memberships and retailer discovery API hooks;
- loyalty/customer content domain APIs;
- Jest infrastructure;
- existing UI primitives and QR/barcode components.

### Refactor incrementally

- `selectedTenantId` -> global active tenant ownership;
- personal tab layout -> config-driven whitelist navigation;
- hardcoded personal colors -> semantic theme tokens;
- personal home/catalog -> reusable blocks plus data providers;
- tenant-scoped query keys and switch invalidation;
- consumer session cold-start validation when backend contract becomes available.

### Create

- tenant feature/store/provider and persistence adapter;
- mock tenant repository for Stage 1;
- mobile-config contract module and validator;
- last-valid config storage;
- retail theme engine;
- centralized feature-flag policy;
- component registry, block renderer and page renderer;
- integration tests for cross-tenant isolation.

## 17. Recommended next stage boundary

Stage 1 should implement only the architecture foundation using mock data:

1. Introduce an application-wide active tenant model for personal mode.
2. Persist a mock `activeTenantId` in AsyncStorage with safe validation.
3. Add a mock valid `MobileConfig` type/config object, without pretending it is the final
   backend contract.
4. Add personal retail-shell providers for tenant, config, theme and feature flags.
5. Keep current personal screens and navigation functional through compatibility adapters.
6. Add focused unit/integration tests.
7. Do not implement the server-driven block renderer or migrate all screens yet.

Production config API integration must wait for the shared JSON Schema and documented tenant
context contract from the backend/web workstream.

## 18. Stage 0 conclusion

Stage 0 confirms that a rewrite is unnecessary. The safe strategy is to preserve the existing
auth/workspace foundation, promote the current loyalty retailer selection into a proper active
tenant lifecycle, and then layer config/theme/feature/navigation capabilities onto the personal
shell incrementally.
