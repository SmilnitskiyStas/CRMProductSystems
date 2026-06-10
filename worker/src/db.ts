import { Pool } from "pg";

const connectionString =
  process.env.DATABASE_URL ?? "postgresql://postgres:postgres@localhost:5432/shelfguard";

export const db = new Pool({ connectionString });
