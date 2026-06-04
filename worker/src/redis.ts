import IORedis from "ioredis";

const redisUrl = process.env.REDIS_URL ?? "redis://localhost:6380";

export const redisConnection = new IORedis(redisUrl, {
  maxRetriesPerRequest: null,
});
