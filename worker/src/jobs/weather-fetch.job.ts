import { Worker, Job } from "bullmq";
import { redisConnection } from "../redis";
import { db } from "../db";

type MeteoDaily = {
  time: string[];
  temperature_2m_max?: (number | null)[];
  temperature_2m_min?: (number | null)[];
  precipitation_sum?: (number | null)[];
  weathercode?: (number | null)[];
};

async function fetchForecast(lat: number, lon: number): Promise<MeteoDaily> {
  const url =
    `https://api.open-meteo.com/v1/forecast?latitude=${lat}&longitude=${lon}` +
    `&daily=temperature_2m_max,temperature_2m_min,precipitation_sum,weathercode` +
    `&forecast_days=7&timezone=UTC`;
  const res = await fetch(url);
  if (!res.ok) throw new Error(`Open-Meteo HTTP ${res.status}`);
  const body = (await res.json()) as { daily?: MeteoDaily };
  if (!body.daily) throw new Error("Open-Meteo response has no daily block");
  return body.daily;
}

async function runWeatherFetch(): Promise<void> {
  const client = await db.connect();
  try {
    const { rows: stores } = await client.query<{
      id: string;
      name: string;
      latitude: string;
      longitude: string;
    }>(`
      SELECT "Id" AS id, "Name" AS name, "Latitude" AS latitude, "Longitude" AS longitude
      FROM stores
      WHERE "IsActive" AND "Latitude" IS NOT NULL AND "Longitude" IS NOT NULL
    `);

    let upserted = 0;

    for (const store of stores) {
      let daily: MeteoDaily;
      try {
        daily = await fetchForecast(Number(store.latitude), Number(store.longitude));
      } catch (e) {
        console.error(`[weather-fetch] ${store.name}: ${(e as Error).message}`);
        continue;
      }

      for (let i = 0; i < daily.time.length; i++) {
        const tmin = daily.temperature_2m_min?.[i] ?? null;
        const tmax = daily.temperature_2m_max?.[i] ?? null;
        const tavg = tmin !== null && tmax !== null ? Math.round(((tmin + tmax) / 2) * 10) / 10 : null;

        await client.query(
          `INSERT INTO weather_data
             ("StoreId", "Date", "TempMin", "TempMax", "TempAvg", "Precipitation", "WeatherCode", "IsForecast", "FetchedAt")
           VALUES ($1, $2, $3, $4, $5, $6, $7, ($2::date >= CURRENT_DATE), NOW())
           ON CONFLICT ("StoreId", "Date") DO UPDATE SET
             "TempMin" = EXCLUDED."TempMin",
             "TempMax" = EXCLUDED."TempMax",
             "TempAvg" = EXCLUDED."TempAvg",
             "Precipitation" = EXCLUDED."Precipitation",
             "WeatherCode" = EXCLUDED."WeatherCode",
             "IsForecast" = EXCLUDED."IsForecast",
             "FetchedAt" = NOW()`,
          [store.id, daily.time[i], tmin, tmax, tavg,
           daily.precipitation_sum?.[i] ?? null, daily.weathercode?.[i] ?? null],
        );
        upserted++;
      }
    }

    console.log(`[weather-fetch] stores: ${stores.length}, days upserted: ${upserted}`);
  } finally {
    client.release();
  }
}

export function startWeatherFetchWorker(): Worker {
  const worker = new Worker(
    "weather-fetch",
    async (job: Job) => {
      console.log(`[weather-fetch] job ${job.id} started`);
      await runWeatherFetch();
    },
    { connection: redisConnection, concurrency: 1 },
  );

  worker.on("completed", (job) => console.log(`[weather-fetch] job ${job.id} completed`));
  worker.on("failed", (job, err) => console.error(`[weather-fetch] job ${job?.id} failed:`, err.message));

  return worker;
}
