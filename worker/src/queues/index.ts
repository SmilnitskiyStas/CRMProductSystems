import { Queue } from "bullmq";
import { redisConnection } from "../redis";

export const expiryQueue = new Queue("expiry-check", { connection: redisConnection });
export const notificationQueue = new Queue("notifications", { connection: redisConnection });
export const weeklyReportQueue = new Queue("weekly-report", { connection: redisConnection });
export const cleanupQueue = new Queue("cleanup", { connection: redisConnection });
