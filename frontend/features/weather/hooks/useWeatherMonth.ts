import { useQuery } from "@tanstack/react-query";
import { weatherApi } from "../api/weather";

/** Daily weather for one location for a calendar month. Disabled until a single location
 * is selected. Weather data barely moves within a session, so it's cached for 30 minutes. */
export function useWeatherMonth(locationId: string | undefined, year: number, month: number) {
  return useQuery({
    queryKey: ["weather", "month", locationId, year, month],
    queryFn: () => weatherApi.getMonth(locationId!, year, month),
    enabled: !!locationId,
    staleTime: 30 * 60_000,
  });
}
