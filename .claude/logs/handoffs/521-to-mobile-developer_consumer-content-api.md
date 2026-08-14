# Handoff: TASK-521 → mobile-developer — Consumer content API (banners, promotions, catalog)

**From:** backend-developer (TASK-521) **Date:** 2026-08-14
**Context:** Consumer App plan (`C:\Users\stass\.claude\plans\quirky-questing-hoare.md`). This
task built a new public read API backing the mobile consumer app's home feed, which today is
100% hardcoded (`mobile/features/loyalty/news.ts` `CONSUMER_NEWS`,
`mobile/features/shopping/products.ts` `DISCOUNTED_PRODUCTS_PREVIEW`). This doc is everything a
fresh agent needs to wire the real API in — it assumes no memory of the backend session.

## Base URL / client to use

Use `personalApiClient` from `mobile/lib/api-client.ts` (same one `loyaltyApi.ts` already uses
for `/api/consumer/loyalty/*`) for **every** call below — banners, promotions, and catalog
alike, including the anonymous-capable GETs. It already:
- Points at the right `BASE_URL` and prefixes routes with `/consumer/...` matching this API's
  `[Route("api/consumer")]`.
- Auto-attaches `Authorization: Bearer <token>` from `SecureStore` (`PERSONAL_ACCESS_TOKEN_KEY`)
  **if one is stored** — and simply omits the header if not. You do not need a separate
  unauthenticated client for the anonymous-browsing case; the same client covers both, because
  every endpoint below accepts requests with or without that header.

**Do not use the workspace-scoped `apiClient`** — these are all `[AllowAnonymous]` consumer
routes, not staff routes.

## tenantId / storeId — where they come from

Exactly the same values `mobile/app/(personal)/index.tsx` already resolves for the loyalty
calls: `selectedMembership.tenantId` and `selectedMembership.preferredStoreId` (see that file,
line ~84 for how `selectedMembership` itself is derived from `useAuthStore`'s
`consumerUser`/memberships list). If the consumer hasn't picked a store yet, there is no
`storeId` to call these with — all three endpoints below **require** `storeId` (except
`view`/`click`, which don't need it at all).

## Auth summary (one sentence)

No `Authorization` header is required for any endpoint (anonymous marketing-content browsing is
explicitly supported and tested); when `personalApiClient` does have a stored consumer token, it
sends it automatically and `view`/`click` events get attributed to that `ConsumerAccountId`
instead of being recorded anonymously — you don't need to do anything special either way.

---

## 1. `GET /api/consumer/{tenantId}/banners?storeId={locationId}`

Active banners assigned to that store, ordered for display, each with its attached products.

**Example response** (`200`, array — empty array `[]` if none active for that store):
```json
[
  {
    "id": "203de9fc-b053-4355-902e-812932dcfddd",
    "title": "Додаткова знижка на обрані товари",
    "eyebrow": "Знижка із застосунком",
    "description": "Покажіть картку покупця в застосунку та придбайте акційні товари вигідніше.",
    "body": [
      "Для учасників програми лояльності діє спеціальна ціна на товари з переліку нижче.",
      "Ціна в застосунку застосовується після ідентифікації покупця на касі."
    ],
    "terms": [
      "Персональні пропозиції доступні лише авторизованим учасникам програми.",
      "Картку покупця потрібно показати касиру до завершення оплати."
    ],
    "imageUrl": "/uploads/banners/203de9fc-b053-4355-902e-812932dcfddd.jpg",
    "icon": "pricetag-outline",
    "backgroundColor": "#7c2d12",
    "accentColor": "#fdba74",
    "detailMode": "internal",
    "externalUrl": null,
    "validUntil": "2026-08-31T00:00:00Z",
    "sortOrder": 0,
    "products": [
      {
        "id": "d53d63a3-0e0b-4e03-88d1-726231d5aedd",
        "name": "Кава мелена",
        "imageUrl": null,
        "unit": "кг",
        "priceRetail": 143.48
      }
    ]
  }
]
```

**Field mapping → `mobile/features/loyalty/news.ts` `ConsumerNewsItem`:**

| API field | `ConsumerNewsItem` field | Notes |
|---|---|---|
| `id` | `id` | now a real GUID, not a slug like `'welcome-bonus'` |
| `icon` | `icon` | already an Ionicons name string, same as the mock |
| `eyebrow` | `eyebrow` | nullable now (mock always had one) — render nothing/omit the eyebrow line when null |
| `title` | `title` | |
| `description` | `description` | |
| **`body` (already `string[]`)** | `body` | **Server already splits on `\n` and trims/drops empty lines — do NOT call `.split('\n')` again on the client. Assign directly.** |
| **`terms` (already `string[]`)** | `terms` | same as `body` — already an array, assign directly |
| `validUntil` (ISO datetime string, nullable) | `validUntil` (mock: free-text string like `"До 31 серпня 2026 року"`) | **format mismatch** — the mock used a human-readable Ukrainian string, the API gives an ISO datetime or `null` (no end date). Mobile needs to format this (e.g. `null` → "Постійна пропозиція" / "Діє щодня", otherwise format the date). This is the one field that needs real transformation logic, not a rename. |
| `backgroundColor` | `background` | |
| `accentColor` | `accent` | |
| `products` | `promotionProducts` | see product mapping below — array is `[]` not `undefined` when there are no attached products (mock used `undefined`/omitted key) |
| *(none)* | — | `imageUrl`/`detailMode`/`externalUrl` are new fields the mock never had — `imageUrl` is the uploaded banner image (fall back to `icon`+`backgroundColor` rendering when null, matching the plan's stated fallback behavior); `detailMode`/`externalUrl` decide whether tapping the banner should push `news/[id]` internally or open `externalUrl` in the system browser |

**Field mapping → `mobile/features/shopping/products.ts` `NewsPromotionProduct`** (via the
banner's `products` array — note this is a much smaller shape than the mock, several mock fields
have no backend source yet):

| API field (`ConsumerBannerProductDto`) | `NewsPromotionProduct` field | Notes |
|---|---|---|
| `id` | `id` | real Item GUID, not a slug |
| `name` | `name` | |
| `unit` | `unit` | |
| `priceRetail` (nullable) | `regularPrice` | no discounted "appPrice" concept exists for a banner-attached product — banners just show the regular catalog product, unlike promotions (section 3 below) which do carry a discount |
| `imageUrl` | *(new)* | mock had no per-product image; render if present |
| — | `barcode` | **not returned** — Item does have barcodes server-side but this DTO doesn't expose them; ask backend if the barcode-scan lookup flow needs it, don't invent a value client-side |
| — | `appPrice`, `discountPercent` | **not applicable to banner products** — a banner-attached product is not a Discount; if a banner-attached item happens to also have an active Discount, that only shows up via the separate `/promotions` endpoint (section 3), not here. Don't try to merge them client-side. |
| — | `icon`, `background`, `manufacturer`, `countryOfOrigin`, `nutrition` | **not returned by this endpoint at all** — these were mock flavor fields with no backend source. Either drop them from the card UI for real data, or leave them `undefined`/use a generic fallback icon; do not block on requesting new backend fields for a first pass. |

---

## 2. `POST /api/consumer/{tenantId}/banners/{id}/view`

Call this **when a banner is shown to the user** — i.e. when it scrolls into view in the news
carousel on `(personal)/index.tsx` (a per-impression fire-and-forget call; don't await/block UI
on it, don't retry on failure).

**Response:** `204 No Content` on success, `404 { "error": "Banner not found." }` if the id/tenant
pair doesn't resolve (e.g. stale id after the admin deactivated it — safe to ignore in the UI).

## 3. `POST /api/consumer/{tenantId}/banners/{id}/click`

Call this **when the user taps into the banner's detail screen** (`news/[id].tsx` navigation, or
just before opening `externalUrl` when `detailMode === "external"`) — i.e. once per genuine
tap-through, not on every render. Same response shape as `view`.

---

## 4. `GET /api/consumer/{tenantId}/promotions?storeId={locationId}`

Active discounted products for one store — a read projection over the existing `Discount`
entity (unrelated to `DiscountsController`, which is a separate staff-facing API you don't need
to touch).

**Example response** (`200`, array):
```json
[
  {
    "id": "b3c1a2e4-1111-4a11-9999-abcdef123456",
    "productId": "d53d63a3-0e0b-4e03-88d1-726231d5aedd",
    "productName": "Кава мелена",
    "imageUrl": null,
    "unit": "кг",
    "discountPercent": 25.00,
    "priceOriginal": 199.90,
    "priceDiscounted": 149.93,
    "validUntil": "2026-09-01T00:00:00Z"
  }
]
```

**Field mapping → `NewsPromotionProduct`** (this is the closer match — `DISCOUNTED_PRODUCTS_PREVIEW`'s shape):

| API field | `NewsPromotionProduct` field |
|---|---|
| `productId` | `id` |
| `productName` | `name` |
| `unit` | `unit` |
| `priceOriginal` | `regularPrice` |
| `priceDiscounted` | `appPrice` |
| `discountPercent` | `discountPercent` |
| `imageUrl` | *(new — was not on the mock, use if present)* |
| — | `barcode`, `icon`, `background`, `manufacturer`, `countryOfOrigin`, `nutrition` | same gap as banner products above — not returned, same handling advice |

`id` (the top-level Discount id, not `productId`) is not something the mock ever needed —
ignore it unless you need it for a future "why is this price different" detail link.

---

## 5. `GET /api/consumer/{tenantId}/catalog?storeId={locationId}&search=&categoryId=&page=&pageSize=`

**This is new ground — there is no existing mobile screen for "browse the full catalog" today,
not even as a mock.** Do not try to map this onto an existing component; it needs a new screen
built from scratch. `search`/`categoryId`/`page`/`pageSize` are all optional query params
(`page` defaults to 1, `pageSize` defaults to 20, max 100).

**Example response** (`200`, paginated envelope — same shape as every other paginated endpoint in this codebase):
```json
{
  "items": [
    {
      "id": "d53d63a3-0e0b-4e03-88d1-726231d5aedd",
      "name": "Абрикос імпортний ваговий",
      "imageUrl": null,
      "unit": "кг",
      "priceRetail": 143.48,
      "categoryId": "8cadaa9f-6275-4250-881b-0ddeb7b65ab0",
      "categoryName": "Сезонні фрукти",
      "isAvailableAtStore": false
    }
  ],
  "totalCount": 215,
  "page": 1,
  "pageSize": 20,
  "totalPages": 11
}
```

`isAvailableAtStore` is a simple boolean (stock qty > 0 at `storeId`) — treat this as "browse
guidance," not a live/reserved-inventory guarantee (it can go stale between page loads).

---

## Verification this contract was built against

All of the above was live-tested against a real dev database with `curl`, with **no**
`Authorization` header for the GET/view/click calls — confirmed working, including a banner
created through the real admin API showing up correctly (body/terms already split into arrays)
and a view+click roundtrip correctly incrementing `GET /api/banners/{id}/analytics` (admin-side,
not something mobile calls). Cross-tenant isolation was also verified: a request against the
wrong `tenantId` in the route returns an empty list / `404`, never another tenant's data — so
using a stale/wrong `selectedMembership.tenantId` fails safe rather than leaking data.

See `.claude/logs/tasks/521_2026-08-14_banner-backend_backend-developer.md` for the full backend
implementation log if anything here is unclear.
