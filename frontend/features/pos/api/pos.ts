import { api } from "@/lib/api";
import type { ShiftDto, ShiftSalesResponse, OpenShiftRequest, CloseShiftRequest } from "../types";

export const posApi = {
  getCurrentShift: () => api.get<ShiftDto>("/api/pos/shifts/current"),

  openShift: (body: OpenShiftRequest) =>
    api.post<ShiftDto>("/api/pos/shifts/open", body),

  // body omitted (or actualClosingCash left undefined) -> old behavior, no reconciliation.
  closeShift: (body?: CloseShiftRequest) =>
    api.post<ShiftDto>("/api/pos/shifts/close", body),

  getShiftSales: (shiftId: string) =>
    api.get<ShiftSalesResponse>(`/api/pos/sales?shiftId=${shiftId}`),
};
