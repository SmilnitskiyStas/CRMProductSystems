import { api } from "@/lib/api";
import type { PagedResult } from "@/lib/api-types";
import type { SupplierDto } from "../types";

export const suppliersApi = {
  getAll: (params?: { include_inactive?: boolean }) => {
    const qs = new URLSearchParams();
    qs.set("include_inactive", params?.include_inactive ? "true" : "false");
    // Backend caps pageSize at 200 (.claude/docs/api-contracts.md) — request the max page
    // size so the dropdown effectively gets "all" suppliers in one call.
    qs.set("pageSize", "200");
    return api
      .get<PagedResult<SupplierDto>>(`/api/suppliers?${qs.toString()}`)
      .then((r) => r.items);
  },
};
