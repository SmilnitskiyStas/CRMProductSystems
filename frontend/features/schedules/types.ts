export type ScheduleStatus = "draft" | "published" | "archived";
export type ShiftStatus = "scheduled" | "confirmed" | "completed" | "cancelled";

export interface WorkScheduleDto {
  id: string;
  locationId: string;
  locationName: string;
  name: string;
  weekStart: string; // YYYY-MM-DD
  status: ScheduleStatus;
  shiftCount: number;
  createdAt: string;
}

export interface ScheduleShiftDto {
  id: string;
  userId: string;
  userName: string;
  locationId: string;
  shiftDate: string; // YYYY-MM-DD
  startTime: string; // HH:mm:ss or HH:mm
  endTime: string;
  breakMinutes: number;
  roleOverride?: string;
  notes?: string;
  status: ShiftStatus;
}

export interface WorkScheduleDetailDto extends WorkScheduleDto {
  shifts: ScheduleShiftDto[];
}

// ── Payloads ──────────────────────────────────────────────────────────────────

export interface CreateSchedulePayload {
  locationId: string;
  name: string;
  weekStart: string; // YYYY-MM-DD, must be a Monday
}

export interface UpdateSchedulePayload {
  name: string;
  status: ScheduleStatus;
}

export interface AddShiftPayload {
  userId: string;
  locationId: string;
  shiftDate: string;
  startTime: string; // HH:mm
  endTime: string;
  breakMinutes: number;
  roleOverride?: string;
  notes?: string;
}

export interface UpdateShiftPayload {
  startTime: string;
  endTime: string;
  breakMinutes: number;
  roleOverride?: string;
  notes?: string;
  status: ShiftStatus;
}
