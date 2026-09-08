import { api } from "@/lib/api";
import type {
  ApplyWeatherPromoRequest,
  ApplyWeatherPromoResult,
  CancelWeatherPromoDiscountsResult,
  WeatherPromoActiveDiscount,
  WeatherPromoSuggestionsResponse,
} from "../types";

export const weatherPromoApi = {
  /**
   * GET /api/ai/weather-promo/suggestions — proactive weekly weather-promo card.
   * `refresh=true` forces a fresh AI call; otherwise the backend's 12h per-tenant cache serves it.
   */
  getSuggestions: (refresh = false) =>
    api.get<WeatherPromoSuggestionsResponse>(
      `/api/ai/weather-promo/suggestions?refresh=${refresh ? "true" : "false"}`,
    ),

  /** POST /api/ai/weather-promo/apply — turn a suggestion into real Discount rows + a calendar promo. */
  apply: (payload: ApplyWeatherPromoRequest) =>
    api.post<ApplyWeatherPromoResult>("/api/ai/weather-promo/apply", payload),

  /** GET /api/ai/weather-promo/active-discounts — running promo discounts for the tenant. */
  getActiveDiscounts: () =>
    api.get<WeatherPromoActiveDiscount[]>("/api/ai/weather-promo/active-discounts"),

  /** POST /api/ai/weather-promo/cancel-discounts — cancel running promo discounts by id. */
  cancelDiscounts: (discountIds: string[]) =>
    api.post<CancelWeatherPromoDiscountsResult>("/api/ai/weather-promo/cancel-discounts", { discountIds }),
};
