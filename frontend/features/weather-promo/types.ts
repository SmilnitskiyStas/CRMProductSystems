// TASK-711 — AI weekly weather-promo suggestions.
// Contract mirrors backend/ShelfGuard.Application/Features/WeatherPromo/Dtos/WeatherPromoDtos.cs
// (camelCase JSON; DateOnly → "yyyy-MM-dd"; DateTime? → ISO string).

export type WeatherPromoStatus =
  | "ok"
  | "not_configured"
  /** No cached result yet; the card shows a "generate" button (an AI call only runs on refresh). */
  | "not_generated"
  | "insufficient_data";

export type WeatherPromoConfidence = "low" | "medium" | "high";

export interface WeatherPromoProduct {
  itemId: string;
  name: string;
  /** Current retail price, or null when the item has no `PriceRetail`. */
  currentPrice: number | null;
  reason: string;
}

export interface WeatherPromoSuggestion {
  title: string;
  /** yyyy-MM-dd */
  startsAt: string;
  /** yyyy-MM-dd */
  endsAt: string;
  weatherSummary: string;
  rationale: string;
  recommendedDiscountPct: number;
  confidence: WeatherPromoConfidence;
  products: WeatherPromoProduct[];
}

export interface WeatherPromoSuggestionsResponse {
  status: WeatherPromoStatus;
  /** ISO timestamp — null unless `status === "ok"`. */
  generatedAt: string | null;
  /** AI model id — null unless `status === "ok"`. */
  model: string | null;
  suggestions: WeatherPromoSuggestion[];
}

export interface ApplyWeatherPromoRequest {
  title: string;
  /** yyyy-MM-dd */
  startsAt: string;
  /** yyyy-MM-dd */
  endsAt: string;
  discountPct: number;
  /** Empty → network-scope event + discounts in every active location. */
  storeIds: string[];
  /** Must be non-empty. */
  productIds: string[];
  createCalendarEvent: boolean;
  createDiscounts: boolean;
}

export interface ApplyWeatherPromoResult {
  eventId: string | null;
  discountIds: string[];
  warnings: string[];
}

/** A running `promo` discount — the panel lists these so a manager can cancel a campaign. */
export interface WeatherPromoActiveDiscount {
  id: string;
  productName: string;
  storeName: string;
  discountPercent: number;
  priceOriginal: number | null;
  priceDiscounted: number | null;
  /** yyyy-MM-dd */
  validFrom: string;
  /** yyyy-MM-dd or null */
  validUntil: string | null;
}

export interface CancelWeatherPromoDiscountsResult {
  cancelled: number;
  warnings: string[];
}
