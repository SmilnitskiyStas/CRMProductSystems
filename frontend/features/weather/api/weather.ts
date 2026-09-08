import { api } from "@/lib/api";
import type { WeatherDay } from "../types";

export const weatherApi = {
  /** GET /api/weather/{locationId}/month — daily weather for a whole calendar month.
   * Returns `[]` when the location has no coordinates or the month falls outside the
   * serviceable window (only stored days come back then). */
  getMonth: (locationId: string, year: number, month: number) =>
    api.get<WeatherDay[]>(`/api/weather/${locationId}/month?year=${year}&month=${month}`),
};
