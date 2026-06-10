import type { ConnectionOptions } from "bullmq";

const redisUrl = process.env.REDIS_URL ?? "redis://localhost:6380";

function parseRedisUrl(url: string): ConnectionOptions {
  const parsed = new URL(url);
  return {
    host:     parsed.hostname || "localhost",
    port:     parseInt(parsed.port || "6379", 10),
    password: parsed.password || undefined,
    db:       parsed.pathname ? parseInt(parsed.pathname.slice(1) || "0", 10) : 0,
    maxRetriesPerRequest: null,
  };
}

export const redisConnection: ConnectionOptions = parseRedisUrl(redisUrl);
