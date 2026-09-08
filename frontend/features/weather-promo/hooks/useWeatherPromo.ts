import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { weatherPromoApi } from "../api/weatherPromo";
import type {
  ApplyWeatherPromoRequest,
  WeatherPromoActiveDiscount,
  WeatherPromoSuggestionsResponse,
} from "../types";

const SUGGESTIONS_KEY = ["weather-promo", "suggestions"] as const;
const ACTIVE_DISCOUNTS_KEY = ["weather-promo", "active-discounts"] as const;

/**
 * Reads the current weekly weather-promo suggestions. The backend caches the answer 12h per
 * tenant, so `staleTime: Infinity` — the panel refreshes explicitly via {@link useRefreshWeatherPromo}.
 * `retry: false` so a 403 (no `inventory` module / no tenant) or 502 (AI failed) surfaces at once
 * and the card can self-hide.
 *
 * `enabled: false` turns it into a pure cache reader that never initiates a fetch — the dashboard
 * teaser passes this so only the `/events` panel (or the explicit refresh button) ever spends an
 * AI call.
 */
export function useWeatherPromoSuggestions(options: { enabled?: boolean } = {}) {
  return useQuery<WeatherPromoSuggestionsResponse>({
    queryKey: SUGGESTIONS_KEY,
    queryFn: () => weatherPromoApi.getSuggestions(false),
    staleTime: Infinity,
    retry: false,
    enabled: options.enabled ?? true,
  });
}

/** Forces a fresh AI generation (`refresh=true`) and writes the result straight into the cache. */
export function useRefreshWeatherPromo() {
  const qc = useQueryClient();
  return useMutation<WeatherPromoSuggestionsResponse, Error, void>({
    mutationFn: () => weatherPromoApi.getSuggestions(true),
    onSuccess: (data) => qc.setQueryData(SUGGESTIONS_KEY, data),
  });
}

/** Applies a suggestion → real Discount rows + a calendar DemandEvent promo. */
export function useApplyWeatherPromo() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: ApplyWeatherPromoRequest) => weatherPromoApi.apply(payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["demand-events"] });
      qc.invalidateQueries({ queryKey: SUGGESTIONS_KEY });
      qc.invalidateQueries({ queryKey: ACTIVE_DISCOUNTS_KEY });
    },
  });
}

/** Running `promo` discounts for the tenant — shown in the panel with per-row cancel. */
export function useWeatherPromoActiveDiscounts(options: { enabled?: boolean } = {}) {
  return useQuery<WeatherPromoActiveDiscount[]>({
    queryKey: ACTIVE_DISCOUNTS_KEY,
    queryFn: () => weatherPromoApi.getActiveDiscounts(),
    retry: false,
    enabled: options.enabled ?? true,
  });
}

/** Cancels running promo discounts by id, then refetches the list. */
export function useCancelWeatherPromoDiscounts() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (discountIds: string[]) => weatherPromoApi.cancelDiscounts(discountIds),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ACTIVE_DISCOUNTS_KEY });
      qc.invalidateQueries({ queryKey: ["demand-events"] });
    },
  });
}
