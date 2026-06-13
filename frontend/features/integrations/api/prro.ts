import { api } from "@/lib/api";

// ── Response shapes ────────────────────────────────────────────────────────

export type PrroProvider = "checkbox" | "disabled";

export interface PrroSettings {
  provider: PrroProvider;
  baseUrl: string;
  licenseKey: string;         // masked: "***...{last4}" or empty
  cashierLogin: string;       // masked: "***...{last4}" or empty
  cashierPassword: string;    // "••••••••" if set, empty otherwise
  cashierPinCode: string;     // "••••••••" if set, empty otherwise
}

export interface UpdatePrroSettingsRequest {
  provider: PrroProvider;
  baseUrl: string;
  licenseKey: string;         // send masked placeholder to keep existing secret
  cashierLogin: string;
  cashierPassword: string;
  cashierPinCode: string;
}

export interface PrroTestResult {
  reachable: boolean;
  fiscalNumber: string;
  isTest: boolean;
  cashierOk: boolean;
  error?: string;
}

// ── API functions ──────────────────────────────────────────────────────────

export async function fetchPrroSettings(): Promise<PrroSettings> {
  return api.get<PrroSettings>("/api/settings/prro");
}

export async function updatePrroSettings(body: UpdatePrroSettingsRequest): Promise<PrroSettings> {
  return api.put<PrroSettings>("/api/settings/prro", body);
}

export async function testPrroConnection(): Promise<PrroTestResult> {
  return api.post<PrroTestResult>("/api/settings/prro/test");
}

// ── Checkbox base URL options ──────────────────────────────────────────────

export const CHECKBOX_BASE_URLS = {
  test: "https://api.checkbox.in.ua/api/v1",
  prod: "https://api.checkbox.ua/api/v1",
} as const;

export type CheckboxEnv = keyof typeof CHECKBOX_BASE_URLS;

export function detectCheckboxEnv(baseUrl: string): CheckboxEnv {
  return baseUrl === CHECKBOX_BASE_URLS.prod ? "prod" : "test";
}
