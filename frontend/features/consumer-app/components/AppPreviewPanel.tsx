"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useBanners } from "../hooks/useBanners";
import { useMobileTheme } from "../hooks/useMobileTheme";
import { usePromoProducts } from "../hooks/usePromoProducts";
import { DEFAULT_DEVICE_PRESET_ID, DEVICE_PRESETS, getDevicePreset, type DevicePresetId } from "./devicePresets";
import { PHONE_FRAME_BORDER_PX, PhoneFrame } from "./PhoneFrame";
import {
  renderBlockPreview,
  type PreviewBannerItem,
  type PreviewContext,
  type PreviewProductItem,
  type PreviewPromotionItem,
  type PreviewStoreItem,
  type PreviewTokens,
} from "./blockPreviews";
import type {
  BlockDefinitionDto,
  MobileConfigBlockInstance,
  MobileConfigBlockType,
  MobileThemeDto,
  ThemeSpacingPreset,
} from "../types";

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 20,
};

const sectionLabelStyle: React.CSSProperties = {
  color: "#9CA3AF",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: 0.4,
  margin: "0 0 12px",
};

// TASK-567: device picker — same inline `<select>` styling convention as `BlockPropertyEditor.tsx`'s
// `EnumField` (`selectStyle`/`inputStyle`), copied rather than imported since that component's
// styles aren't exported shared infrastructure (this feature area's established pattern — see the
// "Style constants" banner comment at the top of every sibling component in this directory).
const deviceSelectStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "6px 30px 6px 10px",
  color: "#E8EDF5",
  fontSize: 12,
  outline: "none",
  cursor: "pointer",
  appearance: "none",
  backgroundImage:
    "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%236B7280' d='M6 8L1 3h10z'/%3E%3C/svg%3E\")",
  backgroundRepeat: "no-repeat",
  backgroundPosition: "right 10px center",
};

// ── Theme token derivation ───────────────────────────────────────────────────────────────────
// Mirrors `mobile/features/theme/tokens.ts`'s `createRetailThemeTokens` exactly (same
// `readableTextOn`/`mixWithBackground` formulas) so block colors/radii/spacing in this preview
// match what a real device computes from the same saved `MobileThemeDto` — not a fixed
// placeholder palette. Manual mirroring, same convention `THEME_*`/`MOBILE_CONFIG_*` in `../types`
// already accepted for keeping web/mobile in sync by hand (no shared package between the trees).

const SPACING_PRESETS: Record<ThemeSpacingPreset, PreviewTokens["spacing"]> = {
  compact: { xs: 4, sm: 8, md: 12, lg: 16, xl: 24 },
  comfortable: { xs: 4, sm: 8, md: 16, lg: 24, xl: 32 },
};

function readableTextOn(hex: string): "#000000" | "#FFFFFF" {
  const red = Number.parseInt(hex.slice(1, 3), 16);
  const green = Number.parseInt(hex.slice(3, 5), 16);
  const blue = Number.parseInt(hex.slice(5, 7), 16);
  const luminance = (0.299 * red + 0.587 * green + 0.114 * blue) / 255;
  return luminance > 0.6 ? "#000000" : "#FFFFFF";
}

function mixWithBackground(hex: string): string {
  const channels = [1, 3, 5].map((index) => Number.parseInt(hex.slice(index, index + 2), 16));
  return `#${channels
    .map((channel) => Math.round(channel * 0.15 + 255 * 0.85).toString(16).padStart(2, "0"))
    .join("")}`;
}

function buildTokens(theme: MobileThemeDto): PreviewTokens {
  return {
    colors: {
      primary: theme.primaryColor,
      secondary: theme.secondaryColor,
      background: theme.backgroundColor,
      surface: theme.surfaceColor,
      textPrimary: theme.textPrimaryColor,
      textSecondary: theme.textSecondaryColor,
      onPrimary: readableTextOn(theme.primaryColor),
      border: mixWithBackground(theme.textSecondaryColor),
    },
    radius: { button: theme.buttonRadius, card: theme.cardRadius },
    spacing: SPACING_PRESETS[theme.spacingPreset] ?? SPACING_PRESETS.comfortable,
  };
}

interface AppPreviewPanelProps {
  blocks: MobileConfigBlockInstance[];
  registryByType: Map<MobileConfigBlockType, BlockDefinitionDto>;
  /** TASK-565: called exactly once per drag gesture by the 4 resizable block previews. Omit to
   *  render every preview read-only (no grab handles). */
  onResizeCommit?: (blockId: string, propName: string, value: number) => void;
}

/**
 * TASK-564: read-only (TASK-565: live-editable) preview column for the App Builder canvas
 * (`AppBuilderCanvas.tsx`) — an Elementor-style "what will this actually look like" panel,
 * entirely client-side (ADR-031): every data source here is a GET the admin already has access to
 * (`useMobileTheme`, `useBanners`, `usePromoProducts`, `useCatalogProducts`, `useLocations`), and
 * `blocks` is the caller's own in-memory (pre-save) draft — never a round trip to a "preview"
 * endpoint.
 *
 * Page-agnostic by design: takes `blocks`/`registryByType` as props rather than reading the whole
 * `configDoc` itself, so `AppBuilderCanvas` decides which page's blocks are being shown.
 */
export function AppPreviewPanel({ blocks, registryByType, onResizeCommit }: AppPreviewPanelProps) {
  const t = useTranslations("Dashboard.consumerApp.appBuilder.preview");
  const themeQuery = useMobileTheme();
  const bannersQuery = useBanners();
  const catalogQuery = useCatalogProducts();
  const locationsQuery = useLocations();

  // TASK-567: which real device's screen dimensions the frame below renders at. Pure display
  // state, not part of the draft document — deliberately local (not persisted, not synced to the
  // backend, resets to the default on reload), same as every other purely-visual choice already
  // living in this panel's own component state.
  const [deviceId, setDeviceId] = useState<DevicePresetId>(DEFAULT_DEVICE_PRESET_ID);
  const device = getDevicePreset(deviceId);
  const framePadding = 16;
  // Frame renders border-box at exactly `device.height` (see `PhoneFrame`'s `width`/`height` prop
  // docs) — the space actually available to children is that total minus the frame's own chrome
  // (its border on both sides, plus the padding passed to it on both sides), so the scrollable
  // block list below always matches "this device's screen", not an arbitrary constant.
  const scrollAreaMaxHeight = device.height - PHONE_FRAME_BORDER_PX * 2 - framePadding * 2;

  // ADR-031: App Builder has no store selector — the tenant's first location stands in for
  // preview purposes only (a preview-only convenience, not a real store-selection UI). Renders
  // gracefully with an empty promotions list when the tenant has zero locations yet.
  const storeId = locationsQuery.data?.[0]?.id ?? null;
  const promoQuery = usePromoProducts(storeId);

  const productById = useMemo(() => {
    const map = new Map<string, { name: string; unit: string; imageUrl: string | null }>();
    for (const p of catalogQuery.data ?? []) map.set(p.id, { name: p.name, unit: p.unit, imageUrl: p.imageUrl });
    return map;
  }, [catalogQuery.data]);

  const banners: PreviewBannerItem[] = useMemo(
    () =>
      (bannersQuery.data ?? [])
        .filter((b) => b.isCurrentlyActive)
        .map((b) => ({
          id: b.id,
          title: b.title,
          subtitle: b.description || undefined,
          imageUrl: b.imageUrl ?? undefined,
          validUntil: b.validUntil ?? undefined,
        })),
    [bannersQuery.data],
  );

  const promotions: PreviewPromotionItem[] = useMemo(
    () =>
      (promoQuery.data ?? [])
        .filter((d) => d.status === "active")
        .map((d) => {
          const product = productById.get(d.productId);
          return {
            id: d.id,
            title: product?.name ?? d.productId,
            subtitle: d.priceDiscounted != null ? `${d.priceDiscounted.toFixed(2)} ₴` : product?.unit,
            imageUrl: product?.imageUrl ?? undefined,
            badge: `−${d.discountPercent}%`,
          };
        }),
    [promoQuery.data, productById],
  );

  const catalog: PreviewProductItem[] = useMemo(
    () =>
      (catalogQuery.data ?? [])
        .filter((p) => p.isActive && p.priceRetail !== null)
        .map((p) => ({
          id: p.id,
          name: p.name,
          price: p.priceRetail as number,
          unit: p.unit,
          imageUrl: p.imageUrl ?? undefined,
        })),
    [catalogQuery.data],
  );

  const locations: PreviewStoreItem[] = useMemo(
    () =>
      (locationsQuery.data ?? [])
        .filter((l) => l.isActive)
        .map((l) => ({ id: l.id, name: l.name, address: l.address ?? undefined })),
    [locationsQuery.data],
  );

  if (themeQuery.isLoading) {
    return <div style={{ ...cardStyle, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>;
  }
  if (themeQuery.isError || !themeQuery.data) {
    return <div style={{ ...cardStyle, color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>;
  }

  const tokens = buildTokens(themeQuery.data);
  const ctx: PreviewContext = { tokens, banners, promotions, catalog, locations, registryByType, onResizeCommit };

  return (
    <div style={cardStyle}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8, marginBottom: 12 }}>
        <p style={{ ...sectionLabelStyle, margin: 0 }}>{t("title")}</p>
        {/* TASK-567: switches which real device's screen dimensions the frame below renders at —
            local state only, updates the frame size and scroll area instantly, no backend round-trip
            (same pattern as everything else in this panel). */}
        <select
          value={deviceId}
          onChange={(e) => setDeviceId(e.target.value as DevicePresetId)}
          aria-label={t("deviceLabel")}
          style={deviceSelectStyle}
        >
          {DEVICE_PRESETS.map((preset) => (
            <option key={preset.id} value={preset.id}>
              {preset.label}
            </option>
          ))}
        </select>
      </div>
      <PhoneFrame background={tokens.colors.background} padding={framePadding} width={device.width} height={device.height}>
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: tokens.spacing.md,
            maxHeight: scrollAreaMaxHeight,
            overflowY: "auto",
          }}
        >
          {blocks.length === 0 ? (
            <p style={{ color: tokens.colors.textSecondary, fontSize: 12, textAlign: "center", padding: "24px 0" }}>
              {t("emptyBlocks")}
            </p>
          ) : (
            blocks.map((block) => renderBlockPreview(block, ctx))
          )}
        </div>
      </PhoneFrame>
      {!storeId && <p style={{ color: "#4B5563", fontSize: 11, marginTop: 10 }}>{t("noStoreHint")}</p>}
    </div>
  );
}
