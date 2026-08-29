"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { toast } from "sonner";
import { Switch } from "@/components/ui/switch";
import { Btn } from "@/components/ui/Btn";
import { useLoyaltySettings, useUpdateLoyaltySettings } from "../hooks/useLoyaltySettings";
import type { CustomerCodeFormat } from "../types";
import { resetAllBonusBalances } from "../api/loyaltySettings";
import { useCategories } from "@/features/inventory/hooks/useCategories";
import { useCatalogProducts, useCatalogProductsByIds } from "@/features/catalog/hooks/useCatalog";
import { CategoryExclusionTree } from "./CategoryExclusionTree";
import { ConfirmDialog } from "./ConfirmDialog";
import { useUnsavedChangesGuard } from "../hooks/useUnsavedChangesGuard";

// ── Style constants (matches PrroConfigModal / ChangePasswordForm conventions —
// this feature has no prior settings UI of its own, so it follows the closest existing
// analog: PrroSettingsController's frontend counterpart) ──────────────────────────────

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 24,
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

const hintStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 11,
  marginTop: 4,
};

const fieldWrapStyle: React.CSSProperties = { marginTop: 16 };

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  cursor: "pointer",
  appearance: "none",
  backgroundImage:
    "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%236B7280' d='M6 8L1 3h10z'/%3E%3C/svg%3E\")",
  backgroundRepeat: "no-repeat",
  backgroundPosition: "right 12px center",
  paddingRight: 36,
};

interface FieldErrors {
  bonusUnitsPerCurrencyUnit?: string;
  redemptionCapPercent?: string;
  minRedemptionBalance?: string;
  codeTtlSeconds?: string;
}

/**
 * TASK-500: "Бонусна програма" card on the Consumer App page — the first (and today, only)
 * section of that page. All 5 pre-existing LoyaltyProgramSettings fields (never had a UI before
 * this task) plus the new `customerCodeFormat` field (TASK-499 backend contract). Numeric bounds
 * mirror LoyaltyService.UpsertSettingsAsync's server-side validation exactly (accrual/cap 0-100,
 * min balance >= 0, TTL 5-300s) so the client never round-trips an obviously-invalid value.
 */
export function BonusProgramSection({ section = "general" }: { section?: "general" | "expiration" | "exclusions" | "rewards" | "lifetime" }) {
  const t = useTranslations("Dashboard.consumerApp.bonusProgram");
  const { data: settings, isLoading, isError } = useLoyaltySettings();
  const update = useUpdateLoyaltySettings();
  const categories = useCategories();

  const [isEnabled, setIsEnabled] = useState(false);
  const [accrualRatePercent, setAccrualRatePercent] = useState("3");
  const [bonusUnitsPerCurrencyUnit, setBonusUnitsPerCurrencyUnit] = useState("1");
  const [redemptionCapPercent, setRedemptionCapPercent] = useState("50");
  const [minRedemptionBalance, setMinRedemptionBalance] = useState("0");
  const [codeTtlSeconds, setCodeTtlSeconds] = useState("30");
  const [customerCodeFormat, setCustomerCodeFormat] = useState<CustomerCodeFormat>("barcode");
  const [annualResetEnabled, setAnnualResetEnabled] = useState(false);
  const [annualResetMonth, setAnnualResetMonth] = useState("1");
  const [annualResetDay, setAnnualResetDay] = useState("1");
  const [annualResetHour, setAnnualResetHour] = useState("0");
  const [bonusResetTimeZone, setBonusResetTimeZone] = useState("Europe/Kyiv");
  const [bonusExclusionsEnabled, setBonusExclusionsEnabled] = useState(false);
  const [exclusionsApplyToAccrual, setExclusionsApplyToAccrual] = useState(true);
  const [exclusionsApplyToRedemption, setExclusionsApplyToRedemption] = useState(true);
  const [excludeDiscountedItems, setExcludeDiscountedItems] = useState(false);
  const [excludedCategoryIds, setExcludedCategoryIds] = useState<string[]>([]);
  const [excludedProductIds, setExcludedProductIds] = useState<string[]>([]);
  const [welcomeRewardEnabled, setWelcomeRewardEnabled] = useState(false);
  const [welcomeRewardAmount, setWelcomeRewardAmount] = useState("0");
  const [firstPurchaseRewardEnabled, setFirstPurchaseRewardEnabled] = useState(false);
  const [firstPurchaseRewardAmount, setFirstPurchaseRewardAmount] = useState("0");
  const [bonusLifetimeEnabled, setBonusLifetimeEnabled] = useState(false);
  const [bonusLifetimeDays, setBonusLifetimeDays] = useState("365");
  const [productSearch, setProductSearch] = useState("");
  const products = useCatalogProducts({ search: productSearch || undefined });
  const selectedProducts = useCatalogProductsByIds(excludedProductIds);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [isResetting, setIsResetting] = useState(false);
  const [showResetConfirm, setShowResetConfirm] = useState(false);

  // Pre-fill from whatever GetSettingsAsync returned — including its own proposed defaults
  // (3%/50%/0/30s/barcode, enabled) for a tenant that never saved a row, so this effect needs
  // no separate "is this new" branch of its own.
  useEffect(() => {
    if (!settings) return;
    setIsEnabled(settings.isEnabled);
    setAccrualRatePercent(String(settings.accrualRatePercent));
    setBonusUnitsPerCurrencyUnit(String(settings.bonusUnitsPerCurrencyUnit ?? 1));
    setRedemptionCapPercent(String(settings.redemptionCapPercent));
    setMinRedemptionBalance(String(settings.minRedemptionBalance));
    setCodeTtlSeconds(String(settings.codeTtlSeconds));
    setCustomerCodeFormat(settings.customerCodeFormat);
    if (settings.annualBonusResetEnabled !== undefined) setAnnualResetEnabled(settings.annualBonusResetEnabled);
    if (settings.annualBonusResetMonth !== undefined) setAnnualResetMonth(String(settings.annualBonusResetMonth));
    if (settings.annualBonusResetDay !== undefined) setAnnualResetDay(String(settings.annualBonusResetDay));
    if (settings.annualBonusResetHour !== undefined) setAnnualResetHour(String(settings.annualBonusResetHour));
    if (settings.bonusResetTimeZone !== undefined) setBonusResetTimeZone(settings.bonusResetTimeZone);
    if (settings.bonusExclusionsEnabled !== undefined) setBonusExclusionsEnabled(settings.bonusExclusionsEnabled);
    if (settings.exclusionsApplyToAccrual !== undefined) setExclusionsApplyToAccrual(settings.exclusionsApplyToAccrual);
    if (settings.exclusionsApplyToRedemption !== undefined) setExclusionsApplyToRedemption(settings.exclusionsApplyToRedemption);
    if (settings.excludeDiscountedItems !== undefined) setExcludeDiscountedItems(settings.excludeDiscountedItems);
    if (settings.excludedCategoryIds !== undefined) setExcludedCategoryIds(settings.excludedCategoryIds);
    if (settings.excludedProductIds !== undefined) setExcludedProductIds(settings.excludedProductIds);
    if (settings.welcomeRewardEnabled !== undefined) setWelcomeRewardEnabled(settings.welcomeRewardEnabled);
    if (settings.welcomeRewardAmount !== undefined) setWelcomeRewardAmount(String(settings.welcomeRewardAmount));
    if (settings.firstPurchaseRewardEnabled !== undefined) setFirstPurchaseRewardEnabled(settings.firstPurchaseRewardEnabled);
    if (settings.firstPurchaseRewardAmount !== undefined) setFirstPurchaseRewardAmount(String(settings.firstPurchaseRewardAmount));
    if (settings.bonusLifetimeEnabled !== undefined) setBonusLifetimeEnabled(settings.bonusLifetimeEnabled);
    if (settings.bonusLifetimeDays !== undefined) setBonusLifetimeDays(String(settings.bonusLifetimeDays));
  }, [settings]);

  function validate(): boolean {
    const e: FieldErrors = {};
    const cap = Number(redemptionCapPercent);
    const conversionRate = Number(bonusUnitsPerCurrencyUnit);
    const minBalance = Number(minRedemptionBalance);
    const ttl = Number(codeTtlSeconds);

    if (!Number.isInteger(conversionRate) || conversionRate < 1 || conversionRate > 1_000_000) {
      e.bonusUnitsPerCurrencyUnit = "Вкажіть ціле число від 1 до 1 000 000";
    }
    if (!Number.isFinite(cap) || cap < 0 || cap > 100) {
      e.redemptionCapPercent = t("percentRangeError");
    }
    if (!Number.isFinite(minBalance) || minBalance < 0) {
      e.minRedemptionBalance = t("nonNegativeError");
    }
    if (!Number.isInteger(ttl) || ttl < 5 || ttl > 300) {
      e.codeTtlSeconds = t("ttlRangeError");
    }

    setErrors(e);
    return Object.keys(e).length === 0;
  }

  function clearError(field: keyof FieldErrors) {
    setErrors((prev) => {
      if (!(field in prev)) return prev;
      const next = { ...prev };
      delete next[field];
      return next;
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!validate() || update.isPending) return;

    try {
      await update.mutateAsync({
        isEnabled,
        accrualRatePercent: Number(accrualRatePercent),
        bonusUnitsPerCurrencyUnit: Number(bonusUnitsPerCurrencyUnit),
        redemptionCapPercent: Number(redemptionCapPercent),
        minRedemptionBalance: Number(minRedemptionBalance),
        codeTtlSeconds: Number(codeTtlSeconds),
        customerCodeFormat,
        annualBonusResetEnabled: annualResetEnabled,
        annualBonusResetMonth: Number(annualResetMonth),
        annualBonusResetDay: Number(annualResetDay),
        annualBonusResetHour: Number(annualResetHour),
        bonusResetTimeZone,
        bonusExclusionsEnabled,
        exclusionsApplyToAccrual,
        exclusionsApplyToRedemption,
        excludeDiscountedItems,
        excludedCategoryIds,
        excludedProductIds,
        welcomeRewardEnabled, welcomeRewardAmount: Number(welcomeRewardAmount),
        firstPurchaseRewardEnabled, firstPurchaseRewardAmount: Number(firstPurchaseRewardAmount),
        profileCompletionRewardEnabled: false, profileCompletionRewardAmount: 0,
        reviewRewardEnabled: false, reviewRewardAmount: 0,
        bonusLifetimeEnabled, bonusLifetimeDays: Number(bonusLifetimeDays),
      });
      toast.success(t("saveSuccess"));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t("saveError"));
    }
  }

  async function handleResetAll() {
    setIsResetting(true);
    try { const result = await resetAllBonusBalances(); toast.success(`Обнулено балансів: ${result.affectedMemberships}`); setShowResetConfirm(false); }
    catch (err) { toast.error(err instanceof Error ? err.message : "Не вдалося обнулити бонуси"); }
    finally { setIsResetting(false); }
  }

  const currentFingerprint = JSON.stringify({ isEnabled, bonusUnitsPerCurrencyUnit, redemptionCapPercent, minRedemptionBalance, codeTtlSeconds, customerCodeFormat, annualResetEnabled, annualResetMonth, annualResetDay, annualResetHour, bonusResetTimeZone, bonusExclusionsEnabled, exclusionsApplyToAccrual, exclusionsApplyToRedemption, excludeDiscountedItems, excludedCategoryIds: [...excludedCategoryIds].sort(), excludedProductIds: [...excludedProductIds].sort(), welcomeRewardEnabled, welcomeRewardAmount, firstPurchaseRewardEnabled, firstPurchaseRewardAmount, bonusLifetimeEnabled, bonusLifetimeDays });
  const savedFingerprint = settings ? JSON.stringify({ isEnabled: settings.isEnabled, bonusUnitsPerCurrencyUnit: String(settings.bonusUnitsPerCurrencyUnit ?? 1), redemptionCapPercent: String(settings.redemptionCapPercent), minRedemptionBalance: String(settings.minRedemptionBalance), codeTtlSeconds: String(settings.codeTtlSeconds), customerCodeFormat: settings.customerCodeFormat, annualResetEnabled: settings.annualBonusResetEnabled ?? false, annualResetMonth: String(settings.annualBonusResetMonth ?? 1), annualResetDay: String(settings.annualBonusResetDay ?? 1), annualResetHour: String(settings.annualBonusResetHour ?? 0), bonusResetTimeZone: settings.bonusResetTimeZone ?? "Europe/Kyiv", bonusExclusionsEnabled: settings.bonusExclusionsEnabled ?? false, exclusionsApplyToAccrual: settings.exclusionsApplyToAccrual ?? true, exclusionsApplyToRedemption: settings.exclusionsApplyToRedemption ?? true, excludeDiscountedItems: settings.excludeDiscountedItems ?? false, excludedCategoryIds: [...(settings.excludedCategoryIds ?? [])].sort(), excludedProductIds: [...(settings.excludedProductIds ?? [])].sort(), welcomeRewardEnabled: settings.welcomeRewardEnabled ?? false, welcomeRewardAmount: String(settings.welcomeRewardAmount ?? 0), firstPurchaseRewardEnabled: settings.firstPurchaseRewardEnabled ?? false, firstPurchaseRewardAmount: String(settings.firstPurchaseRewardAmount ?? 0), bonusLifetimeEnabled: settings.bonusLifetimeEnabled ?? false, bonusLifetimeDays: String(settings.bonusLifetimeDays ?? 365) }) : currentFingerprint;
  useUnsavedChangesGuard(currentFingerprint !== savedFingerprint, "Є незбережені зміни у бонусній програмі. Залишити сторінку?");

  if (isLoading) {
    return (
      <div style={{ ...cardStyle, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>
    );
  }

  if (isError) {
    return (
      <div style={{ ...cardStyle, color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>
    );
  }

  return (
    <div style={cardStyle}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 20 }}>
        <span style={{ fontSize: 24 }}>🎁</span>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>
            {section === "general" ? t("title") : section === "expiration" ? "Строк дії та обнулення бонусів" : section === "exclusions" ? "Винятки бонусної програми" : section === "rewards" ? "Винагороди" : "Строк життя бонусів"}
          </h2>
          <p style={{ color: "#4B5563", fontSize: 12, margin: 0, marginTop: 3 }}>
            {section === "general" ? t("subtitle") : section === "expiration" ? "Керуйте щорічним списанням у локальному часі підприємства." : section === "exclusions" ? "Виберіть позиції, для яких бонуси не нараховуються або не списуються." : section === "rewards" ? "Автоматичні одноразові бонуси за підтверджені дії покупця." : "Кожне нове нарахування матиме власну дату завершення, а витрачатимуться найстаріші бонуси."}
          </p>
        </div>
      </div>

      <form onSubmit={handleSubmit}>
        {section === "general" && <>
        {/* Enabled toggle */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            gap: 12,
          }}
        >
          <div>
            <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>
              {t("enabledLabel")}
            </div>
            <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>
              {t("enabledHint")}
            </div>
          </div>
          <Switch checked={isEnabled} onCheckedChange={setIsEnabled} />
        </div>

        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))",
            gap: "0 16px",
          }}
        >
          <div style={fieldWrapStyle}>
            <label style={labelStyle}>Скільки бонусів дорівнює 1 грн</label>
            <input
              type="number"
              min={1}
              max={1_000_000}
              step={1}
              value={bonusUnitsPerCurrencyUnit}
              onChange={(e) => {
                setBonusUnitsPerCurrencyUnit(e.target.value);
                clearError("bonusUnitsPerCurrencyUnit");
              }}
              style={{ ...inputStyle, borderColor: errors.bonusUnitsPerCurrencyUnit ? "#EF4444" : "#374151" }}
            />
            {errors.bonusUnitsPerCurrencyUnit && <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.bonusUnitsPerCurrencyUnit}</p>}
            <p style={hintStyle}>Наприклад: 1 — це 1 бонус = 1 грн; 100 — це 100 бонусів = 1 грн.</p>
          </div>

        {/* Redemption cap */}
          <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("redemptionCapLabel")}</label>
          <input
            type="number"
            min={0}
            max={100}
            step="0.1"
            value={redemptionCapPercent}
            onChange={(e) => {
              setRedemptionCapPercent(e.target.value);
              clearError("redemptionCapPercent");
            }}
            style={{
              ...inputStyle,
              borderColor: errors.redemptionCapPercent ? "#EF4444" : "#374151",
            }}
          />
          {errors.redemptionCapPercent && (
            <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.redemptionCapPercent}</p>
          )}
          <p style={hintStyle}>{t("redemptionCapHint")}</p>
          </div>

        {/* Min redemption balance */}
          <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("minRedemptionBalanceLabel")}</label>
          <input
            type="number"
            min={0}
            step="0.01"
            value={minRedemptionBalance}
            onChange={(e) => {
              setMinRedemptionBalance(e.target.value);
              clearError("minRedemptionBalance");
            }}
            style={{
              ...inputStyle,
              borderColor: errors.minRedemptionBalance ? "#EF4444" : "#374151",
            }}
          />
          {errors.minRedemptionBalance && (
            <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.minRedemptionBalance}</p>
          )}
          <p style={hintStyle}>{t("minRedemptionBalanceHint")}</p>
          </div>

        {/* Code TTL */}
          <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("codeTtlLabel")}</label>
          <input
            type="number"
            min={5}
            max={300}
            step="1"
            value={codeTtlSeconds}
            onChange={(e) => {
              setCodeTtlSeconds(e.target.value);
              clearError("codeTtlSeconds");
            }}
            style={{
              ...inputStyle,
              borderColor: errors.codeTtlSeconds ? "#EF4444" : "#374151",
            }}
          />
          {errors.codeTtlSeconds && (
            <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.codeTtlSeconds}</p>
          )}
          <p style={hintStyle}>{t("codeTtlHint")}</p>
          </div>

        {/* Customer code format (TASK-500) */}
          <div style={fieldWrapStyle}>
          <label style={labelStyle}>{t("codeFormatLabel")}</label>
          <select
            value={customerCodeFormat}
            onChange={(e) => setCustomerCodeFormat(e.target.value as CustomerCodeFormat)}
            style={selectStyle}
          >
            <option value="qr">{t("codeFormatQr")}</option>
            <option value="barcode">{t("codeFormatBarcode")}</option>
          </select>
          <p style={hintStyle}>{t("codeFormatHint")}</p>
          </div>
        </div>
        </>}

        {section === "expiration" && <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 16 }}>
            <div><div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>Щорічно обнуляти бонуси</div><p style={hintStyle}>У вибрану дату всі невикористані бонуси покупців будуть списані з записом в історії.</p></div>
            <Switch checked={annualResetEnabled} onCheckedChange={setAnnualResetEnabled} />
          </div>
          {annualResetEnabled && <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 240px))", gap: 12, marginTop: 16 }}>
            <div><label style={labelStyle}>Місяць</label><select value={annualResetMonth} onChange={(e) => setAnnualResetMonth(e.target.value)} style={selectStyle}>{Array.from({ length: 12 }, (_, i) => <option key={i + 1} value={i + 1}>{new Intl.DateTimeFormat("uk-UA", { month: "long" }).format(new Date(2024, i, 1))}</option>)}</select></div>
            <div><label style={labelStyle}>День</label><input type="number" min={1} max={31} value={annualResetDay} onChange={(e) => setAnnualResetDay(e.target.value)} style={inputStyle} /></div>
            <div><label style={labelStyle}>Година виконання</label><select value={annualResetHour} onChange={(e) => setAnnualResetHour(e.target.value)} style={selectStyle}>{Array.from({ length: 24 }, (_, hour) => <option key={hour} value={hour}>{String(hour).padStart(2, "0")}:00</option>)}</select></div>
            <div><label style={labelStyle}>Часовий пояс</label><select value={bonusResetTimeZone} onChange={(e) => setBonusResetTimeZone(e.target.value)} style={selectStyle}><option value="Europe/Kyiv">Київ</option><option value="Europe/Warsaw">Варшава</option><option value="Europe/Berlin">Берлін</option><option value="UTC">UTC</option></select></div>
          </div>}
          {settings?.lastAnnualBonusResetYear && <p style={hintStyle}>Останнє автоматичне обнулення: {settings.lastAnnualBonusResetYear} рік</p>}
          <div style={{ marginTop: 16 }}><Btn type="button" variant="ghost" disabled={isResetting} onClick={() => setShowResetConfirm(true)}>{isResetting ? "Обнулення…" : "Обнулити бонуси всім зараз"}</Btn></div>
        </div>}

        {section === "exclusions" && <div>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 16 }}><div><div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>Застосовувати винятки</div><p style={hintStyle}>Якщо вимкнено, всі товари беруть участь у бонусній програмі.</p></div><Switch checked={bonusExclusionsEnabled} onCheckedChange={setBonusExclusionsEnabled} /></div>
          {bonusExclusionsEnabled && <>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(240px, 1fr))", gap: 12, marginTop: 18 }}>
              <Requirement checked={exclusionsApplyToAccrual} onChange={setExclusionsApplyToAccrual} title="Не нараховувати бонуси" />
              <Requirement checked={exclusionsApplyToRedemption} onChange={setExclusionsApplyToRedemption} title="Не дозволяти списання бонусів" />
              <Requirement checked={excludeDiscountedItems} onChange={setExcludeDiscountedItems} title="Виключати всі акційні товари" />
            </div>
            <div style={{ marginTop: 20 }}><label style={labelStyle}>Виключені категорії</label><CategoryExclusionTree categories={categories.data ?? []} selectedIds={excludedCategoryIds} onChange={setExcludedCategoryIds} /></div>
            <div style={{ marginTop: 20 }}><label style={labelStyle}>Конкретні виключені товари</label><div style={{ display: "flex", flexWrap: "wrap", gap: 8, marginBottom: 10 }}>{(selectedProducts.data ?? []).map((product) => <span key={product.id} style={{ display: "flex", alignItems: "center", gap: 6, background: "#161B26", border: "1px solid #374151", color: "#D1D5DB", borderRadius: 7, padding: "6px 8px", fontSize: 12 }}>{product.name}<button type="button" aria-label="Прибрати товар" onClick={() => setExcludedProductIds((current) => current.filter((id) => id !== product.id))} style={{ border: 0, background: "transparent", color: "#6B7280", cursor: "pointer", padding: 0, display: "flex" }}><X size={13} /></button></span>)}</div><input value={productSearch} onChange={(e) => setProductSearch(e.target.value)} placeholder="Пошук товару за назвою" style={inputStyle} />{productSearch && <div style={{ maxHeight: 220, overflowY: "auto", marginTop: 6 }}>{(products.data ?? []).filter((product) => !excludedProductIds.includes(product.id)).slice(0, 12).map((product) => <button type="button" key={product.id} onClick={() => { setExcludedProductIds((current) => [...current, product.id]); setProductSearch(""); }} style={{ width: "100%", textAlign: "left", border: 0, borderBottom: "1px solid #1F2937", background: "#0D1117", color: "#D1D5DB", padding: "9px 10px", cursor: "pointer" }}>{product.name}</button>)}</div>}</div>
          </>}
        </div>}

        {section === "rewards" && <div style={{ display: "grid", gap: 12 }}>
          <RewardSetting title="За приєднання до програми" hint="Нараховується один раз під час створення бонусної картки." enabled={welcomeRewardEnabled} onEnabled={setWelcomeRewardEnabled} amount={welcomeRewardAmount} onAmount={setWelcomeRewardAmount} />
          <RewardSetting title="За першу покупку" hint="Нараховується один раз після першого успішного продажу з бонусною карткою." enabled={firstPurchaseRewardEnabled} onEnabled={setFirstPurchaseRewardEnabled} amount={firstPurchaseRewardAmount} onAmount={setFirstPurchaseRewardAmount} />
        </div>}

        {section === "lifetime" && <div><div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 16 }}><div><div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>Обмежувати строк життя нових бонусів</div><p style={hintStyle}>Налаштування застосовується лише до нових нарахувань. Старі бонуси не зникнуть.</p></div><Switch checked={bonusLifetimeEnabled} onCheckedChange={setBonusLifetimeEnabled} /></div>{bonusLifetimeEnabled && <div style={{ marginTop: 16, maxWidth: 260 }}><label style={labelStyle}>Строк життя, днів</label><input type="number" min={1} max={3650} value={bonusLifetimeDays} onChange={(e) => setBonusLifetimeDays(e.target.value)} style={inputStyle} /><p style={hintStyle}>Під час оплати першими витрачаються бонуси з найближчою датою завершення.</p></div>}</div>}

        {/* Actions */}
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 12,
            marginTop: 24,
            paddingTop: 20,
            borderTop: "1px solid #1F2937",
          }}
        >
          <Btn type="submit" disabled={update.isPending}>
            {update.isPending ? t("savingButton") : t("saveButton")}
          </Btn>
          {settings?.updatedAt && (
            <span style={{ color: "#4B5563", fontSize: 12 }}>
              {t("lastUpdatedPrefix")} {new Date(settings.updatedAt).toLocaleString()}
            </span>
          )}
        </div>
      </form>
      {showResetConfirm && <ConfirmDialog title="Обнулити всі бонусні баланси?" description="Усі невикористані бонуси покупців будуть списані. Скасувати цю дію неможливо, але кожне списання залишиться в історії." confirmLabel={isResetting ? "Обнулення…" : "Обнулити бонуси"} cancelLabel="Скасувати" variant="danger" pending={isResetting} onConfirm={handleResetAll} onClose={() => setShowResetConfirm(false)} />}
    </div>
  );
}

function Requirement({ checked, onChange, title }: { checked: boolean; onChange: (checked: boolean) => void; title: string }) { return <div style={{ display: "flex", alignItems: "center", gap: 10, background: "#161B26", borderRadius: 8, padding: 12 }}><Switch checked={checked} onCheckedChange={onChange} /><span style={{ color: "#D1D5DB", fontSize: 12 }}>{title}</span></div>; }
function RewardSetting({ title, hint: text, enabled, onEnabled, amount, onAmount }: { title: string; hint: string; enabled: boolean; onEnabled: (value: boolean) => void; amount: string; onAmount: (value: string) => void }) { return <div style={{ background: "#161B26", border: "1px solid #293241", borderRadius: 9, padding: 14 }}><div style={{ display: "flex", justifyContent: "space-between", gap: 12 }}><div><div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{title}</div><p style={hintStyle}>{text}</p></div><Switch checked={enabled} onCheckedChange={onEnabled} /></div>{enabled && <div style={{ marginTop: 10, maxWidth: 240 }}><label style={labelStyle}>Сума винагороди, бонусів</label><input type="number" min={0} step="0.01" value={amount} onChange={(e) => onAmount(e.target.value)} style={inputStyle} /></div>}</div>; }
