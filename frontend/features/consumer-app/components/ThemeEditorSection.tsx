"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useForm, type UseFormRegister, type UseFormSetValue } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Check, Info, Palette, SlidersHorizontal } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { extractThemeValidationErrors } from "../api/mobileTheme";
import { useMobileTheme, useUpdateMobileTheme } from "../hooks/useMobileTheme";
import { useUnsavedChangesGuard } from "../hooks/useUnsavedChangesGuard";
import { PhoneFrame } from "./PhoneFrame";
import { SectionTabs } from "./SectionTabs";
import {
  THEME_BUTTON_RADIUS_MAX,
  THEME_BUTTON_RADIUS_MIN,
  THEME_CARD_RADIUS_MAX,
  THEME_CARD_RADIUS_MIN,
  THEME_HEX_COLOR_PATTERN,
  THEME_LOGO_URL_MAX_LENGTH,
  THEME_SPACING_PRESETS,
  type ThemeSpacingPreset,
  type UpdateMobileThemeRequest,
} from "../types";

// ── Style constants (matches BonusProgramSection / BannerForm conventions — this feature has
// no shadcn form primitives of its own, every sibling section in this directory uses the same
// inline dark-card style) ──────────────────────────────────────────────────────────────────

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

const hintStyle: React.CSSProperties = { color: "#4B5563", fontSize: 11, marginTop: 4 };

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

// ── Schema (mirrors backend/ShelfGuard.Application/Features/MobileConfig/MobileThemeWhitelists.cs
// and MobileThemeValidator.cs exactly, via the THEME_* constants in ../types) ──────────────────

function isValidHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}

function buildSchema(t: ReturnType<typeof useTranslations>) {
  const hex = z.string().regex(THEME_HEX_COLOR_PATTERN, t("hexColorError"));
  return z.object({
    logoUrl: z
      .string()
      .max(THEME_LOGO_URL_MAX_LENGTH, t("logoUrlLengthError"))
      .refine((v) => v.trim() === "" || isValidHttpUrl(v.trim()), { message: t("logoUrlFormatError") }),
    primaryColor: hex,
    secondaryColor: hex,
    backgroundColor: hex,
    surfaceColor: hex,
    textPrimaryColor: hex,
    textSecondaryColor: hex,
    buttonRadius: z
      .number({ invalid_type_error: t("radiusRequiredError") })
      .int(t("radiusIntegerError"))
      .min(THEME_BUTTON_RADIUS_MIN, t("radiusRangeError", { min: THEME_BUTTON_RADIUS_MIN, max: THEME_BUTTON_RADIUS_MAX }))
      .max(THEME_BUTTON_RADIUS_MAX, t("radiusRangeError", { min: THEME_BUTTON_RADIUS_MIN, max: THEME_BUTTON_RADIUS_MAX })),
    cardRadius: z
      .number({ invalid_type_error: t("radiusRequiredError") })
      .int(t("radiusIntegerError"))
      .min(THEME_CARD_RADIUS_MIN, t("radiusRangeError", { min: THEME_CARD_RADIUS_MIN, max: THEME_CARD_RADIUS_MAX }))
      .max(THEME_CARD_RADIUS_MAX, t("radiusRangeError", { min: THEME_CARD_RADIUS_MIN, max: THEME_CARD_RADIUS_MAX })),
    spacingPreset: z.enum(THEME_SPACING_PRESETS),
  });
}

type FormValues = z.infer<ReturnType<typeof buildSchema>>;

// MobileTheme.CreateDefault's built-in defaults (backend/ShelfGuard.Domain/Entities/MobileTheme.cs)
// — used as the form's defaultValues before GET resolves, so a first paint never shows blank/
// invalid fields; useMobileTheme's GetThemeAsync proposes these exact values for a tenant that
// never saved a theme, so this is not a guess.
const DEFAULT_VALUES: FormValues = {
  logoUrl: "",
  primaryColor: "#1E40AF",
  secondaryColor: "#222222",
  backgroundColor: "#FFFFFF",
  surfaceColor: "#F7F7F7",
  textPrimaryColor: "#111111",
  textSecondaryColor: "#777777",
  buttonRadius: 14,
  cardRadius: 18,
  spacingPreset: "comfortable",
};

type ThemePreset = { id: "light" | "dark" | "minimal" | "bright" | "premium"; values: Omit<FormValues, "logoUrl"> };
const THEME_PRESETS: ThemePreset[] = [
  { id: "light", values: { primaryColor: "#2563EB", secondaryColor: "#0F172A", backgroundColor: "#F8FAFC", surfaceColor: "#FFFFFF", textPrimaryColor: "#0F172A", textSecondaryColor: "#64748B", buttonRadius: 14, cardRadius: 18, spacingPreset: "comfortable" } },
  { id: "dark", values: { primaryColor: "#60A5FA", secondaryColor: "#A78BFA", backgroundColor: "#090E17", surfaceColor: "#111827", textPrimaryColor: "#F8FAFC", textSecondaryColor: "#94A3B8", buttonRadius: 14, cardRadius: 18, spacingPreset: "comfortable" } },
  { id: "minimal", values: { primaryColor: "#18181B", secondaryColor: "#52525B", backgroundColor: "#FFFFFF", surfaceColor: "#F4F4F5", textPrimaryColor: "#18181B", textSecondaryColor: "#71717A", buttonRadius: 6, cardRadius: 8, spacingPreset: "compact" } },
  { id: "bright", values: { primaryColor: "#F97316", secondaryColor: "#EC4899", backgroundColor: "#FFF7ED", surfaceColor: "#FFFFFF", textPrimaryColor: "#431407", textSecondaryColor: "#9A3412", buttonRadius: 18, cardRadius: 22, spacingPreset: "comfortable" } },
  { id: "premium", values: { primaryColor: "#D4AF37", secondaryColor: "#7C3AED", backgroundColor: "#100F14", surfaceColor: "#1C1924", textPrimaryColor: "#FFF8DC", textSecondaryColor: "#B8AFA1", buttonRadius: 10, cardRadius: 16, spacingPreset: "comfortable" } },
];

const THEME_FORM_FIELDS = [
  "logoUrl",
  "primaryColor",
  "secondaryColor",
  "backgroundColor",
  "surfaceColor",
  "textPrimaryColor",
  "textSecondaryColor",
  "buttonRadius",
  "cardRadius",
  "spacingPreset",
] as const;

function isThemeFormField(field: string): field is (typeof THEME_FORM_FIELDS)[number] {
  return (THEME_FORM_FIELDS as readonly string[]).includes(field);
}

/**
 * TASK-537: "Дизайн" section of the Retailer Admin shell (fills the `/consumer-app/design`
 * placeholder TASK-535 scaffolded), editing the tenant's single MobileTheme row (TASK-536).
 *
 * DRAFT/PUBLISH STATE (TASK-544b, updated after TASK-544 reconciled theme into the generalized
 * publish flow): a successful PUT here saves immediately, but only to the tenant's *pending*
 * theme. `GET /api/v1/mobile/config` now reads `theme` from the tenant's last *published*
 * `MobileConfigurationVersion`, so real consumers only see this change after the tenant's next
 * `POST /api/v1/mobile/config/publish` — same as every other part of the document. The inline
 * notice near the Save button states this and links to `/consumer-app/versions` (TASK-546), where
 * the actual Publish action lives — there is deliberately no "Publish" button in this component
 * itself, since publishing is a whole-document action, not specific to the theme.
 */
export function ThemeEditorSection() {
  const t = useTranslations("Dashboard.consumerApp.themeEditor");
  const { data: theme, isLoading, isError } = useMobileTheme();
  const update = useUpdateMobileTheme();
  const [formError, setFormError] = useState<string | null>(null);
  const [designTab, setDesignTab] = useState<"presets" | "custom">("presets");

  const schema = useMemo(() => buildSchema(t), [t]);

  const {
    register,
    handleSubmit,
    reset,
    setValue,
    setError,
    watch,
    formState: { errors, isDirty },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: DEFAULT_VALUES,
  });

  // TASK-546: warns before losing unsaved edits (tab-close/refresh always; in-app link-click
  // navigation best-effort) — see useUnsavedChangesGuard.ts's remarks for exact coverage.
  useUnsavedChangesGuard(isDirty, t("unsavedChangesWarning"));

  // Pre-fill from whatever GetThemeAsync returned — including its own proposed defaults for a
  // tenant that never saved a row — same convention BonusProgramSection uses for its settings.
  useEffect(() => {
    if (!theme) return;
    reset({
      logoUrl: theme.logoUrl ?? "",
      primaryColor: theme.primaryColor,
      secondaryColor: theme.secondaryColor,
      backgroundColor: theme.backgroundColor,
      surfaceColor: theme.surfaceColor,
      textPrimaryColor: theme.textPrimaryColor,
      textSecondaryColor: theme.textSecondaryColor,
      buttonRadius: theme.buttonRadius,
      cardRadius: theme.cardRadius,
      spacingPreset: theme.spacingPreset,
    });
  }, [theme, reset]);

  // Watched (not-yet-saved) values feed both the color-swatch inputs and the live preview panel.
  const values = watch();

  function applyPreset(preset: ThemePreset) {
    for (const [field, value] of Object.entries(preset.values) as Array<[keyof ThemePreset["values"], string | number]>) {
      setValue(field, value as never, { shouldDirty: true, shouldValidate: true });
    }
    toast.success(t("presetApplied"));
  }

  async function onValid(formValues: FormValues) {
    setFormError(null);
    if (update.isPending) return;

    const body: UpdateMobileThemeRequest = {
      logoUrl: formValues.logoUrl.trim() === "" ? null : formValues.logoUrl.trim(),
      primaryColor: formValues.primaryColor,
      secondaryColor: formValues.secondaryColor,
      backgroundColor: formValues.backgroundColor,
      surfaceColor: formValues.surfaceColor,
      textPrimaryColor: formValues.textPrimaryColor,
      textSecondaryColor: formValues.textSecondaryColor,
      buttonRadius: formValues.buttonRadius,
      cardRadius: formValues.cardRadius,
      spacingPreset: formValues.spacingPreset,
    };

    try {
      await update.mutateAsync(body);
      toast.success(t("saveSuccess"));
    } catch (err) {
      // MobileThemeController's 400 body is structured per-field (MobileConfigValidationError[]),
      // not the codebase's usual flat `{ error: string }` — attach each one to its exact form
      // field instead of a single generic banner (TASK-537 DoD).
      const fieldErrors = extractThemeValidationErrors(err);
      if (fieldErrors) {
        let mappedAny = false;
        for (const fe of fieldErrors) {
          if (isThemeFormField(fe.field)) {
            setError(fe.field, { type: "server", message: fe.message });
            mappedAny = true;
          }
        }
        if (!mappedAny) {
          setFormError(fieldErrors.map((e) => e.message).join(" "));
        }
      } else {
        setFormError(err instanceof Error ? err.message : t("saveError"));
      }
    }
  }

  if (isLoading) {
    return <div style={{ ...cardStyle, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>;
  }
  if (isError) {
    return <div style={{ ...cardStyle, color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>;
  }

  return (
    <div>
      <SectionTabs items={[
        { key: "presets" as const, label: t("presetsTab"), icon: <Palette size={14} /> },
        { key: "custom" as const, label: t("customTab"), icon: <SlidersHorizontal size={14} /> },
      ]} activeKey={designTab} onChange={setDesignTab} ariaLabel={t("designTabsLabel")} />
      <div style={{ display: "flex", gap: 24, flexWrap: "wrap", alignItems: "flex-start" }}>
      {designTab === "presets" ? <div style={{ ...cardStyle, flex: "1 1 680px" }}>
        <div style={{ marginBottom: 16 }}><h2 style={{ color: "#E8EDF5", fontSize: 15, margin: 0 }}>{t("presetsTitle")}</h2><p style={{ ...hintStyle, marginBottom: 0 }}>{t("presetsHint")}</p></div>
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(210px, 1fr))", gap: 12 }}>
          {THEME_PRESETS.map((preset) => {
            const selected = Object.entries(preset.values).every(([key, value]) => values[key as keyof FormValues] === value);
            return <article key={preset.id} style={{ overflow: "hidden", border: `1px solid ${selected ? "#3B82F6" : "#293241"}`, borderRadius: 12, background: "#10151D", boxShadow: selected ? "0 0 0 1px rgba(59,130,246,.25)" : "none" }}>
              <div style={{ height: 128, padding: 14, background: preset.values.backgroundColor, boxSizing: "border-box" }}><div style={{ height: "100%", borderRadius: preset.values.cardRadius, background: preset.values.surfaceColor, padding: 12, boxSizing: "border-box", display: "flex", flexDirection: "column", justifyContent: "space-between" }}><div><div style={{ width: "58%", height: 8, borderRadius: 5, background: preset.values.textPrimaryColor }} /><div style={{ width: "76%", height: 5, borderRadius: 4, background: preset.values.textSecondaryColor, marginTop: 7, opacity: .75 }} /></div><div style={{ display: "flex", gap: 6 }}><span style={{ width: 34, height: 34, borderRadius: 9, background: preset.values.primaryColor }} /><span style={{ flex: 1, height: 34, borderRadius: preset.values.buttonRadius, background: preset.values.secondaryColor }} /></div></div></div>
              <div style={{ padding: 12 }}><div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 8 }}><div><strong style={{ color: "#E8EDF5", fontSize: 13 }}>{t(`preset_${preset.id}_name`)}</strong><p style={{ ...hintStyle, marginBottom: 0 }}>{t(`preset_${preset.id}_description`)}</p></div>{selected && <span title={t("selectedPreset")} style={{ width: 24, height: 24, borderRadius: "50%", display: "grid", placeItems: "center", background: "#2563EB", color: "white", flexShrink: 0 }}><Check size={14} /></span>}</div><button type="button" onClick={() => applyPreset(preset)} style={{ width: "100%", marginTop: 11, border: `1px solid ${selected ? "#2563EB" : "#374151"}`, borderRadius: 8, padding: "8px 10px", background: selected ? "rgba(37,99,235,.14)" : "#111827", color: selected ? "#93C5FD" : "#D1D5DB", fontSize: 12, fontWeight: 600, cursor: "pointer" }}>{selected ? t("presetSelectedButton") : t("applyPresetButton")}</button></div>
            </article>;
          })}
        </div>
        <div style={{ display: "flex", alignItems: "center", gap: 8, marginTop: 16, paddingTop: 16, borderTop: "1px solid #1F2937" }}><Btn type="button" onClick={() => setDesignTab("custom")}>{t("customizePresetButton")}</Btn><span style={{ color: "#4B5563", fontSize: 11 }}>{t("presetDraftNotice")}</span></div>
      </div> : <div style={{ ...cardStyle, flex: "1 1 680px" }}>
        <form onSubmit={handleSubmit(onValid)}>
          {/* Logo */}
          <div>
            <label style={labelStyle}>{t("logoUrlLabel")}</label>
            <input
              {...register("logoUrl")}
              placeholder={t("logoUrlPlaceholder")}
              style={{ ...inputStyle, borderColor: errors.logoUrl ? "#EF4444" : "#374151" }}
            />
            {errors.logoUrl && <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.logoUrl.message}</p>}
            <p style={hintStyle}>{t("logoUrlHint")}</p>
          </div>

          {/* Colors */}
          <div style={fieldWrapStyle}>
            <label style={labelStyle}>{t("colorsGroupLabel")}</label>
            <div style={{ display: "grid", gridTemplateColumns: "repeat(2, 1fr)", gap: 12 }}>
              <ColorField
                label={t("primaryColorLabel")}
                name="primaryColor"
                register={register}
                setValue={setValue}
                value={values.primaryColor}
                error={errors.primaryColor?.message}
              />
              <ColorField
                label={t("secondaryColorLabel")}
                name="secondaryColor"
                register={register}
                setValue={setValue}
                value={values.secondaryColor}
                error={errors.secondaryColor?.message}
              />
              <ColorField
                label={t("backgroundColorLabel")}
                name="backgroundColor"
                register={register}
                setValue={setValue}
                value={values.backgroundColor}
                error={errors.backgroundColor?.message}
              />
              <ColorField
                label={t("surfaceColorLabel")}
                name="surfaceColor"
                register={register}
                setValue={setValue}
                value={values.surfaceColor}
                error={errors.surfaceColor?.message}
              />
              <ColorField
                label={t("textPrimaryColorLabel")}
                name="textPrimaryColor"
                register={register}
                setValue={setValue}
                value={values.textPrimaryColor}
                error={errors.textPrimaryColor?.message}
              />
              <ColorField
                label={t("textSecondaryColorLabel")}
                name="textSecondaryColor"
                register={register}
                setValue={setValue}
                value={values.textSecondaryColor}
                error={errors.textSecondaryColor?.message}
              />
            </div>
          </div>

          {/* Radius */}
          <div style={{ ...fieldWrapStyle, display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
            <div>
              <label style={labelStyle}>{t("buttonRadiusLabel")}</label>
              <input
                type="number"
                min={THEME_BUTTON_RADIUS_MIN}
                max={THEME_BUTTON_RADIUS_MAX}
                step={1}
                {...register("buttonRadius", { valueAsNumber: true })}
                style={{ ...inputStyle, borderColor: errors.buttonRadius ? "#EF4444" : "#374151" }}
              />
              {errors.buttonRadius && (
                <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.buttonRadius.message}</p>
              )}
              <p style={hintStyle}>
                {t("buttonRadiusHint", { min: THEME_BUTTON_RADIUS_MIN, max: THEME_BUTTON_RADIUS_MAX })}
              </p>
            </div>
            <div>
              <label style={labelStyle}>{t("cardRadiusLabel")}</label>
              <input
                type="number"
                min={THEME_CARD_RADIUS_MIN}
                max={THEME_CARD_RADIUS_MAX}
                step={1}
                {...register("cardRadius", { valueAsNumber: true })}
                style={{ ...inputStyle, borderColor: errors.cardRadius ? "#EF4444" : "#374151" }}
              />
              {errors.cardRadius && (
                <p style={{ ...hintStyle, color: "#EF4444" }}>{errors.cardRadius.message}</p>
              )}
              <p style={hintStyle}>
                {t("cardRadiusHint", { min: THEME_CARD_RADIUS_MIN, max: THEME_CARD_RADIUS_MAX })}
              </p>
            </div>
          </div>

          {/* Spacing */}
          <div style={fieldWrapStyle}>
            <label style={labelStyle}>{t("spacingLabel")}</label>
            <select {...register("spacingPreset")} style={selectStyle}>
              {THEME_SPACING_PRESETS.map((preset) => (
                <option key={preset} value={preset}>
                  {t(preset === "compact" ? "spacingCompact" : "spacingComfortable")}
                </option>
              ))}
            </select>
            <p style={hintStyle}>{t("spacingHint")}</p>
          </div>

          {/* Draft/publish notice — saved immediately to the pending theme, visible to consumers
              only after the tenant's next publish; no in-app publish action exists yet (TASK-546) */}
          <div
            style={{
              display: "flex",
              alignItems: "flex-start",
              gap: 8,
              marginTop: 20,
              padding: "10px 12px",
              background: "#1F2937",
              borderRadius: 8,
            }}
          >
            <Info size={15} style={{ color: "#F59E0B", flexShrink: 0, marginTop: 1 }} />
            <p style={{ color: "#D1D5DB", fontSize: 12, margin: 0 }}>
              {t("draftNotice")}{" "}
              <Link href="/consumer-app/versions" style={{ color: "#93C5FD" }}>
                {t("goToVersionsLink")}
              </Link>
            </p>
          </div>

          {formError && <p style={{ color: "#F87171", fontSize: 12, marginTop: 12 }}>{formError}</p>}

          {/* Actions */}
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: 12,
              marginTop: 16,
              paddingTop: 20,
              borderTop: "1px solid #1F2937",
            }}
          >
            <Btn type="submit" disabled={update.isPending}>
              {update.isPending ? t("savingButton") : t("saveButton")}
            </Btn>
            {theme?.updatedAt && (
              <span style={{ color: "#4B5563", fontSize: 12 }}>
                {t("lastUpdatedPrefix")} {new Date(theme.updatedAt).toLocaleString()}
              </span>
            )}
          </div>
        </form>
      </div>}

      {/* Live preview */}
      <div style={{ flex: "0 1 340px", position: "sticky", top: 20 }}>
        <ThemePreview values={values} t={t} />
      </div>
      </div>
    </div>
  );
}

// ── Color field (color swatch + validated hex text input, kept in sync) ────────────────────

type ColorFieldName =
  | "primaryColor"
  | "secondaryColor"
  | "backgroundColor"
  | "surfaceColor"
  | "textPrimaryColor"
  | "textSecondaryColor";

interface ColorFieldProps {
  label: string;
  name: ColorFieldName;
  register: UseFormRegister<FormValues>;
  setValue: UseFormSetValue<FormValues>;
  value: string;
  error?: string;
}

function ColorField({ label, name, register, setValue, value, error }: ColorFieldProps) {
  // The native color picker always emits a valid 6-digit hex; while the paired text input holds
  // an in-progress/invalid value, the swatch falls back to black rather than breaking.
  const swatchValue = THEME_HEX_COLOR_PATTERN.test(value) ? value : "#000000";

  return (
    <div>
      <label style={labelStyle}>{label}</label>
      <div style={{ display: "flex", gap: 8 }}>
        <input
          type="color"
          value={swatchValue}
          onChange={(e) => setValue(name, e.target.value, { shouldValidate: true, shouldDirty: true })}
          aria-label={label}
          style={{
            width: 42,
            height: 38,
            padding: 2,
            border: "1px solid #374151",
            borderRadius: 8,
            cursor: "pointer",
            background: "#0D1117",
            flexShrink: 0,
          }}
        />
        <input
          {...register(name)}
          placeholder="#000000"
          style={{ ...inputStyle, borderColor: error ? "#EF4444" : "#374151", flex: 1 }}
        />
      </div>
      {error && <p style={{ ...hintStyle, color: "#EF4444" }}>{error}</p>}
    </div>
  );
}

// ── Live preview (a static mock, not the real mobile block renderer — that's Stage C's later
// App Builder work) ─────────────────────────────────────────────────────────────────────────

const SPACING_METRICS: Record<ThemeSpacingPreset, { padding: number; gap: number }> = {
  compact: { padding: 10, gap: 6 },
  comfortable: { padding: 16, gap: 12 },
};

function ThemePreview({
  values,
  t,
}: {
  values: FormValues;
  t: ReturnType<typeof useTranslations>;
}) {
  const metrics = SPACING_METRICS[values.spacingPreset] ?? SPACING_METRICS.comfortable;
  const primary = THEME_HEX_COLOR_PATTERN.test(values.primaryColor) ? values.primaryColor : "#1E40AF";
  const secondary = THEME_HEX_COLOR_PATTERN.test(values.secondaryColor) ? values.secondaryColor : "#222222";
  const background = THEME_HEX_COLOR_PATTERN.test(values.backgroundColor) ? values.backgroundColor : "#FFFFFF";
  const surface = THEME_HEX_COLOR_PATTERN.test(values.surfaceColor) ? values.surfaceColor : "#F7F7F7";
  const textPrimary = THEME_HEX_COLOR_PATTERN.test(values.textPrimaryColor) ? values.textPrimaryColor : "#111111";
  const textSecondary = THEME_HEX_COLOR_PATTERN.test(values.textSecondaryColor)
    ? values.textSecondaryColor
    : "#777777";
  const buttonRadius = Number.isFinite(values.buttonRadius)
    ? Math.min(Math.max(values.buttonRadius, 0), THEME_BUTTON_RADIUS_MAX)
    : 14;
  const cardRadius = Number.isFinite(values.cardRadius)
    ? Math.min(Math.max(values.cardRadius, 0), THEME_CARD_RADIUS_MAX)
    : 18;
  const logoUrl = values.logoUrl.trim();

  return (
    <div>
      <p style={{ ...labelStyle, marginBottom: 10 }}>{t("previewTitle")}</p>
      <PhoneFrame background={background} padding={metrics.padding + 8}>
      <div style={{ display: "flex", flexDirection: "column", gap: metrics.gap }}>
        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          {logoUrl ? (
            // External tenant-supplied logo URL — same next/image opt-out as BannerForm.tsx's
            // identical image-preview pattern (pre-existing `no-img-element` lint warning there).
            <img
              src={logoUrl}
              alt=""
              onError={(e) => {
                (e.currentTarget as HTMLImageElement).style.visibility = "hidden";
              }}
              style={{ width: 26, height: 26, borderRadius: "50%", objectFit: "cover", background: surface }}
            />
          ) : (
            <div style={{ width: 26, height: 26, borderRadius: "50%", background: primary }} />
          )}
          <span style={{ color: textPrimary, fontSize: 13, fontWeight: 700 }}>{t("previewAppName")}</span>
        </div>

        {/* Card */}
        <div style={{ background: surface, borderRadius: cardRadius, padding: metrics.padding }}>
          <div style={{ color: textPrimary, fontSize: 13, fontWeight: 700 }}>{t("previewCardTitle")}</div>
          <div style={{ color: textSecondary, fontSize: 11, marginTop: 4 }}>{t("previewCardBody")}</div>
          <button
            type="button"
            tabIndex={-1}
            style={{
              marginTop: metrics.gap,
              background: primary,
              color: "#FFFFFF",
              border: "none",
              borderRadius: buttonRadius,
              padding: "8px 14px",
              fontSize: 12,
              fontWeight: 600,
              cursor: "default",
            }}
          >
            {t("previewButtonLabel")}
          </button>
        </div>

        {/* Pill */}
        <span
          style={{
            alignSelf: "flex-start",
            background: secondary,
            color: "#FFFFFF",
            fontSize: 10,
            fontWeight: 600,
            borderRadius: 999,
            padding: "4px 10px",
          }}
        >
          {t("previewPillLabel")}
        </span>

        {/* Bottom nav mock */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-around",
            paddingTop: metrics.padding,
            marginTop: metrics.gap,
            borderTop: `1px solid ${surface}`,
          }}
        >
          {[0, 1, 2, 3].map((i) => (
            <div
              key={i}
              style={{
                width: 8,
                height: 8,
                borderRadius: "50%",
                background: i === 0 ? primary : textSecondary,
                opacity: i === 0 ? 1 : 0.4,
              }}
            />
          ))}
        </div>
      </div>
      </PhoneFrame>
    </div>
  );
}
