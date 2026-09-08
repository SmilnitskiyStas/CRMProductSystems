export interface WeatherDay {
  date: string; // yyyy-MM-dd
  tempMin: number | null;
  tempMax: number | null;
  tempAvg: number | null;
  precipitation: number | null;
  weatherCode: number | null;
  isForecast: boolean;
}
