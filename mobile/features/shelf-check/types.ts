export interface ShelfCheckZone {
  id: string;
  name: string;
  type?: string | null;
}

export interface ShelfCheckLocation {
  id: string;
  name: string;
  isActive: boolean;
  zones?: ShelfCheckZone[] | null;
}

export interface PlanogramItem {
  id: string;
  shelfId: string;
  itemId: string;
  itemName: string;
  barcode: string | null;
  expectedFacings: number;
}

export interface PlanogramShelf {
  shelfId: string;
  label: string;
  items: PlanogramItem[];
}

export type ScanItemStatus = 'present' | 'low' | 'missing';

export interface ShelfScanItem {
  itemId: string;
  name: string;
  status: ScanItemStatus;
  estimatedCount: number;
  confidence: number;
  note: string | null;
}

export interface ShelfScan {
  id: string;
  locationId: string;
  zoneId: string;
  shelfId: string | null;
  imageUrl: string;
  status: string;
  missingCount: number;
  lowCount: number;
  alertPending: boolean;
  summary: string | null;
  items: ShelfScanItem[];
  model: string | null;
  errorMessage: string | null;
  createdBy: string | null;
  createdAt: string;
}

export interface PreparedPhoto {
  uri: string;
  width: number;
  height: number;
}
