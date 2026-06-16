import { api } from "@/lib/api";
import type { ModulesSettings } from "../types";

export const modulesApi = {
  /** GET /api/settings/modules — enterprise_admin self-service (own tenant only). */
  get: () => api.get<ModulesSettings>("/api/settings/modules"),
};
