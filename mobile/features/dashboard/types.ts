export interface DashboardStats {
  safe: number;
  warning: number;
  critical: number;
  expired: number;
}

export interface DashboardTask {
  id: string;
  productName: string;
  action: string;
  status: 'warning' | 'critical' | 'expired';
  daysLeft: number;
}
