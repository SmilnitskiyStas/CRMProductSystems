import { api } from "@/lib/api";
import type { ShiftDto, ShiftSalesResponse, OpenShiftRequest } from "../types";

export const posApi = {
  getCurrentShift: () => api.get<ShiftDto>("/api/pos/shifts/current"),

  openShift: (body: OpenShiftRequest) =>
    api.post<ShiftDto>("/api/pos/shifts/open", body),

  closeShift: () => api.post<ShiftDto>("/api/pos/shifts/close"),

  getShiftSales: (shiftId: string) =>
    api.get<ShiftSalesResponse>(`/api/pos/sales?shiftId=${shiftId}`),
};
