// Types mirror ".claude/docs/promotion-rules.md" -> "Phase 2 API contract (FINAL)". JSON camelCase.

export type RuleType = "percent_off" | "fixed_off" | "fixed_price" | "buy_n_get_m";
export type RuleStatus = "draft" | "published" | "paused" | "archived";
export type MatchMode = "per_item" | "across_set";
export type AudienceKind = "all" | "loyalty_member" | "loyalty_tier";

export interface RuleDefinition {
  type: RuleType;
  targets: { itemIds: string[]; categoryIds: string[] };
  scope: {
    storeIds: string[];
    validFrom: string | null;
    validTo: string | null;
    weekdays: number[];
    timeFrom: string | null;
    timeTo: string | null;
  };
  audience: { kind: AudienceKind; tierIds?: string[] };
  params: {
    percent?: number;
    amount?: number;
    price?: number;
    buyQty?: number;
    getQty?: number;
    getPrice?: number;
    getPercent?: number;
    matchMode?: MatchMode;
  };
  limits: { maxApplicationsPerReceipt: number | null };
  excludeAlreadyDiscounted: boolean;
  priority: number;
}

export interface RuleListItem {
  id: string;
  name: string;
  description: string | null;
  status: RuleStatus;
  type: RuleType;
  currentVersionNumber: number;
  publishedVersionNumber: number | null;
  storeIds: string[];
  validFrom: string | null;
  validTo: string | null;
  hasUnpublishedChanges: boolean;
  updatedAt: string;
}

export interface RuleVersion {
  id: string;
  versionNumber: number;
  definition: RuleDefinition;
  changeComment: string | null;
  createdByName: string | null;
  createdAt: string;
}

export interface VersionSummary {
  id: string;
  versionNumber: number;
  createdAt: string;
  createdByName: string | null;
  changeComment: string | null;
  isPublished: boolean;
}

export interface RuleDetail {
  id: string;
  name: string;
  description: string | null;
  status: RuleStatus;
  currentVersion: RuleVersion;
  publishedVersion: RuleVersion | null;
  versions: VersionSummary[];
}

export interface SaveRulePayload {
  name: string;
  description: string | null;
  definition: RuleDefinition;
  changeComment?: string | null;
}

export interface ValidationIssue {
  code: string;
  field: string | null;
  message: string;
}

export interface ValidationResult {
  errors: ValidationIssue[];
  warnings: ValidationIssue[];
}

export interface SimulateLineInput {
  itemId: string;
  quantity: number;
  unitPrice: number | null;
}

export interface SimulatePayload {
  ruleId: string | null;
  definition: RuleDefinition | null;
  storeId: string;
  at: string | null;
  membership: { tierId: string | null } | null;
  lines: SimulateLineInput[];
}

export interface SimulateSegment {
  quantity: number;
  unitPrice: number;
  label: string | null;
}

export interface SimulateLine {
  itemId: string;
  name: string;
  quantity: number;
  regularUnitPrice: number;
  segments: SimulateSegment[];
  lineTotal: number;
  regularLineTotal: number;
}

export interface SimulateResult {
  lines: SimulateLine[];
  total: number;
  regularTotal: number;
  savings: number;
}

export interface LookupItem {
  id: string;
  name: string;
  barcode: string | null;
  priceRetail: number | null;
  categoryId: string | null;
  categoryName: string | null;
}

export interface LookupTier {
  id: string;
  name: string;
  discountPercent: number;
  sortOrder: number;
}

export interface ItemRef {
  id: string;
  name: string;
}
