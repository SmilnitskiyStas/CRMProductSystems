export interface WorkSchedule {
  id: string;
  locationId: string;
  locationName: string;
  name: string;
  weekStart: string; // "YYYY-MM-DD"
  status: string;    // draft | published | archived
  shiftCount: number;
  createdAt: string;
}

export interface WorkScheduleDetail extends WorkSchedule {
  shifts: ScheduleShift[];
}

export interface ScheduleShift {
  id: string;
  userId: string;
  userName: string;
  locationId: string;
  shiftDate: string;        // "YYYY-MM-DD"
  startTime: string;        // "HH:mm:ss"
  endTime: string;          // "HH:mm:ss"
  breakMinutes: number;
  roleOverride: string | null;
  notes: string | null;
  status: string;           // scheduled | confirmed | completed | cancelled
}
