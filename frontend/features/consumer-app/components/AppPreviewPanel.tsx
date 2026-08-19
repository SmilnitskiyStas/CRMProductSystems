"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { Home, type LucideIcon } from "lucide-react";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useBanners } from "../hooks/useBanners";
import { useMobileTheme } from "../hooks/useMobileTheme";
import { usePromoProducts } from "../hooks/usePromoProducts";
import { DEFAULT_DEVICE_PRESET_ID, DEVICE_PRESETS, getDevicePreset, type DevicePresetId } from "./devicePresets";
import { NAVIGATION_ICON_COMPONENTS } from "./NavigationBuilderSection";
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
  MobileConfigBlockType,
  MobileConfigNavigationIcon,
  MobileConfigNavigationItem,
  MobileConfigNavigationType,
  MobileConfigPage,
  MobileConfigPageName,
  MobileThemeDto,
  ThemeSpacingPreset,
} from "../types";

// ── TASK-568: nav item type → App-Builder-editable page mapping ────────────────────────────────
// Source of truth verified directly against `mobile/features/retail-navigation/policy.ts`'s
// `retailRoutePolicies` — only these 4 of the 8 `MobileConfigNavigationType`s route to a screen
// whose content actually comes from the App Builder's `pages` document; the other 4 (`loyalty` →
// wallet, `coupons` → coupons, `stores` → retailers, `profile` → account) are fixed native screens
// with no App Builder involvement at all, so clicking them in this mockup must never show
// fabricated block content (ADR-031's core truthfulness requirement) — a type absent from this map
// falls through to the "not editable here" placeholder, see `nonEditableNavType` state below.
const NAV_TYPE_TO_EDITABLE_PAGE: Partial<Record<MobileConfigNavigationType, MobileConfigPageName>> = {
  home: "home",
  promotions: "promotions",
  catalog: "catalog",
  news: "news",
};

/** Rendered height of the bottom nav bar mockup below, in px — subtracted from the scroll area's
 *  `maxHeight` so the nav bar always has room and is never pushed outside the (overflow-hidden)
 *  frame by a long block list. Matches the icon+label+padding sizes used in the nav bar JSX. */
const NAV_BAR_HEIGHT_PX = 54;

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
  /** TASK-568: every whitelisted page's blocks (was a single page's `blocks` array pre-TASK-568) —
   *  the mockup's own bottom nav (below) lets the admin browse between pages independent of which
   *  `PageTabs` tab is active in the canvas above, so this panel needs every page's content, not
   *  just the active one. */
  pages: Partial<Record<MobileConfigPageName, MobileConfigPage>>;
  /** TASK-568: the tenant's configured navigation — drives the mockup's own bottom tab bar (one
   *  entry per item, 2–5 items). */
  navigation: MobileConfigNavigationItem[];
  /** TASK-568: the canvas's current `PageTabs` selection — the mockup's own nav defaults to
   *  showing this page, and re-syncs to it whenever it changes, until the admin clicks a different
   *  item in the mockup's own nav (see `previewPage` state below). */
  activePage: MobileConfigPageName;
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
 * `pages` is the caller's own in-memory (pre-save) draft — never a round trip to a "preview"
 * endpoint.
 *
 * TASK-568: gained its own bottom nav mockup (mirrors the tenant's real `navigation` config) so the
 * admin can browse pages inside the mockup itself, independent of `AppBuilderCanvas`'s own
 * `PageTabs` — see `previewPage`/`nonEditableNavType` state below.
 */
export function AppPreviewPanel({ pages, navigation, activePage, registryByType, onResizeCommit }: AppPreviewPanelProps) {
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

  // TASK-568: which page's blocks the mockup's content area currently shows — initialized from
  // (and re-synced to) the canvas's own `activePage` on every change, so switching `PageTabs`
  // above still drives the preview by default. Diverges from `activePage` only once the admin
  // clicks a *different* item in the mockup's own bottom nav below (real-user-like browsing,
  // independent of what an admin happens to be editing) — until `activePage` changes again.
  const [previewPage, setPreviewPage] = useState<MobileConfigPageName>(activePage);
  // TASK-568: non-null while the admin has selected one of the 4 nav items with no App-Builder
  // page behind it (loyalty/coupons/stores/profile) — swaps the content area to a "not editable
  // here" placeholder instead of `pages[previewPage]`'s blocks, and marks that nav item (not a
  // page) as the mockup's visually "active" tab. Cleared whenever an editable nav item is clicked,
  // or `activePage` changes (re-sync brings the mockup back to showing real page content).
  const [nonEditableNavType, setNonEditableNavType] = useState<MobileConfigNavigationType | null>(null);

  useEffect(() => {
    setPreviewPage(activePage);
    setNonEditableNavType(null);
  }, [activePage]);

  function handleNavClick(item: MobileConfigNavigationItem) {
    const page = NAV_TYPE_TO_EDITABLE_PAGE[item.type];
    if (page) {
      setPreviewPage(page);
      setNonEditableNavType(null);
    } else {
      setNonEditableNavType(item.type);
    }
  }

  // Frame renders border-box at exactly `device.height` (see `PhoneFrame`'s `width`/`height` prop
  // docs) — the space actually available to children is that total minus the frame's own chrome
  // (its border on both sides, plus the padding passed to it on both sides) and, TASK-568, the
  // bottom nav bar's own height — so the scrollable block list below always matches "this device's
  // screen, minus the tab bar", not an arbitrary constant.
  const scrollAreaMaxHeight =
    device.height - PHONE_FRAME_BORDER_PX * 2 - framePadding * 2 - (navigation.length > 0 ? NAV_BAR_HEIGHT_PX : 0);

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
  const previewBlocks = pages[previewPage]?.blocks ?? [];

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
      {/* TASK-568: `fitToViewport` scales the frame to the vertical room actually available below
          this panel, so the full mockup (including the bottom nav below) is visible without
          forcing the outer dashboard page to scroll. */}
      <PhoneFrame
        background={tokens.colors.background}
        padding={framePadding}
        width={device.width}
        height={device.height}
        fitToViewport
      >
        <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
          <div
            style={{
              display: "flex",
              flexDirection: "column",
              gap: tokens.spacing.md,
              maxHeight: scrollAreaMaxHeight,
              overflowY: "auto",
            }}
          >
            {nonEditableNavType ? (
              <p style={{ color: tokens.colors.textSecondary, fontSize: 12, textAlign: "center", padding: "24px 0" }}>
                {t("navItemNotEditable")}
              </p>
            ) : previewBlocks.length === 0 ? (
              <p style={{ color: tokens.colors.textSecondary, fontSize: 12, textAlign: "center", padding: "24px 0" }}>
                {t("emptyBlocks")}
              </p>
            ) : (
              previewBlocks.map((block) => renderBlockPreview(block, ctx))
            )}
          </div>

          {/* TASK-568: mirrors the tenant's real bottom tab bar (`navigation`) — clicking an
              App-Builder-editable item (home/promotions/catalog/news) switches `previewPage`;
              clicking one of the other 4 (loyalty/coupons/stores/profile, fixed native screens
              with no App Builder involvement) shows the "not editable here" placeholder above
              instead of fabricated content (ADR-031). `marginTop: auto` pins it to the frame's
              bottom edge even when the content above is short; the negative side/bottom margins
              pull it flush to the frame's own padding, matching a real edge-to-edge tab bar. */}
          {navigation.length > 0 && (
            <div
              style={{
                marginTop: "auto",
                marginLeft: -framePadding,
                marginRight: -framePadding,
                marginBottom: -framePadding,
                display: "flex",
                borderTop: `1px solid ${tokens.colors.border}`,
                background: tokens.colors.surface,
              }}
            >
              {navigation.map((item, index) => {
                const Icon: LucideIcon =
                  NAVIGATION_ICON_COMPONENTS[item.icon as MobileConfigNavigationIcon] ?? Home;
                const editablePage = NAV_TYPE_TO_EDITABLE_PAGE[item.type];
                const isActive = nonEditableNavType
                  ? item.type === nonEditableNavType
                  : !!editablePage && editablePage === previewPage;
                const color = isActive ? tokens.colors.primary : tokens.colors.textSecondary;
                return (
                  <button
                    key={`${item.type}-${index}`}
                    type="button"
                    onClick={() => handleNavClick(item)}
                    style={{
                      flex: 1,
                      display: "flex",
                      flexDirection: "column",
                      alignItems: "center",
                      gap: 2,
                      padding: "8px 4px 10px",
                      background: "transparent",
                      border: "none",
                      cursor: "pointer",
                      color,
                    }}
                  >
                    <Icon size={18} />
                    <span
                      style={{
                        fontSize: 10,
                        fontWeight: isActive ? 700 : 500,
                        maxWidth: "100%",
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {item.label}
                    </span>
                  </button>
                );
              })}
            </div>
          )}
        </div>
      </PhoneFrame>
      {!storeId && <p style={{ color: "#4B5563", fontSize: 11, marginTop: 10 }}>{t("noStoreHint")}</p>}
    </div>
  );
}
