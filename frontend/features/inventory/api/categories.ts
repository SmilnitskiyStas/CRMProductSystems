import { api } from "@/lib/api";
import type { CategoryDto } from "../types";

export const categoriesApi = {
  getAll: () => api.get<CategoryDto[]>("/api/categories"),
};
