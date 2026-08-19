"use client";

import { useMemo } from "react";
import { useTranslations } from "next-intl";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useBanners } from "../hooks/useBanners";
import { useMobileTheme } from "../hooks/useMobileTheme";
import { usePromoProducts } from "../hooks/usePromoProducts";
import { PhoneFrame } from "./PhoneFrame";
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
      <p style={sectionLabelStyle}>{t("title")}</p>
      <PhoneFrame background={tokens.colors.background} padding={16}>
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            gap: tokens.spacing.md,
            maxHeight: 600,
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
