// Pure IoT business rules (v3-spec §1, §4) — no I/O, unit-testable.

// ── Weight sensors ─────────────────────────────────────────────────────────

export type WeightAssessment = {
  units: number;       // whole units the delta corresponds to (0 when not a multiple)
  confidence: number;  // 95 exact single / 85 exact multiple / 60 non-multiple
};

// How far from a whole multiple of unit weight we still accept (scale noise)
const MULTIPLE_TOLERANCE = 0.05;

export function assessWeightDelta(deltaGrams: number, unitWeightGrams: number): WeightAssessment {
  if (unitWeightGrams <= 0 || deltaGrams === 0) return { units: 0, confidence: 0 };

  const ratio = Math.abs(deltaGrams) / unitWeightGrams;
  const rounded = Math.round(ratio);
  const isMultiple = rounded >= 1 && Math.abs(ratio - rounded) <= MULTIPLE_TOLERANCE * rounded;

  if (!isMultiple) return { units: 0, confidence: 60 };
  return { units: rounded, confidence: rounded === 1 ? 95 : 85 };
}

// Below this we never touch stock automatically — log only (v3-spec §1)
export const AUTO_WRITE_DOWN_MIN_CONFIDENCE = 70;

// ── Temperature sensors ────────────────────────────────────────────────────

export type TempProfile = "fridge" | "freezer";

// v3-spec §4: fridge (+2..+6) alerts above +8; freezer (-18..-15) alerts above -12
const PROFILE_ALERT_ABOVE: Record<TempProfile, number> = {
  fridge: 8,
  freezer: -12,
};

export type DeviceConfig = {
  profile?: TempProfile;
  alert_above?: number;
  product_id?: string;
  unit_weight_grams?: number;
};

// pg returns jsonb columns as parsed objects; raw strings appear via tests/other drivers
export function parseDeviceConfig(raw: unknown): DeviceConfig {
  if (!raw) return {};
  if (typeof raw === "object") return raw as DeviceConfig;
  if (typeof raw !== "string") return {};
  try {
    return JSON.parse(raw) as DeviceConfig;
  } catch {
    return {};
  }
}

// Explicit alert_above wins; otherwise the profile default; otherwise no alerting
export function tempAlertThreshold(config: DeviceConfig): number | null {
  if (typeof config.alert_above === "number") return config.alert_above;
  if (config.profile && config.profile in PROFILE_ALERT_ABOVE) {
    return PROFILE_ALERT_ABOVE[config.profile];
  }
  return null;
}

export function isTempAlert(temperature: number, threshold: number | null): boolean {
  return threshold !== null && temperature > threshold;
}

// Sustained violation window before batches are flagged (v3-spec §4: > 2 hours)
export const TEMP_VIOLATION_HOURS = 2;
// Device silent for this long → offline alert (v3-spec §4: > 30 min)
export const OFFLINE_AFTER_MINUTES = 30;
