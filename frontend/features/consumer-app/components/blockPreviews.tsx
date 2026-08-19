"use client";

import { useEffect, useState } from "react";
import type { CSSProperties } from "react";
import { useResizeDrag } from "../hooks/useResizeDrag";
import type {
  BlockDefinitionDto,
  MobileConfigBlockInstance,
  MobileConfigBlockType,
} from "../types";

// ── TASK-564: web-native "mirror" components for the App Builder's live preview column
// (AppPreviewPanel.tsx). Plain React + inline styles, one per `MobileConfigBlockType` — matches
// `mobile/features/server-driven-ui/blocks/CoreBlocks.tsx`'s exact current visual proportions
// (280/210/170px carousel card widths, 190px hero minHeight, 48%/31% grid percent-widths) rather
// than reusing the real React Native components (ADR-031: a permanent RN-web maintenance surface
// for an admin-only, deliberately-approximate preview isn't worth it — same boundary
// `ThemeEditorSection.tsx`'s `ThemePreview` already drew for the theme mockup).
//
// Every component here reads its own block's raw *authored* `props` (the exact object the admin
// is editing, pre-save) plus a shared `PreviewContext` of already-fetched, already-mapped data —
// no network calls happen in this file. Where a block's real mobile renderer (resolveBlocks.ts +
// CoreBlocks.tsx) transforms authored props before display (limit slicing, `columns` 2/3 mapping,
// `cardWidthPx`/`heightPx` fallback), the equivalent transform is reproduced here so the preview
// stays truthful to what mobile will actually show — not a nicer-looking invented version.

// ── Design tokens ────────────────────────────────────────────────────────────────────────────
// Mirrors `mobile/features/theme/tokens.ts`'s `createRetailThemeTokens` exactly (same
// `readableTextOn`/`mixWithBackground` formulas), derived by `AppPreviewPanel` from the tenant's
// `MobileThemeDto` — so block colors/radii/spacing in this preview match what a real device would
// compute from the same saved theme, not a fixed placeholder palette.

export interface PreviewTokens {
  colors: {
    primary: string;
    secondary: string;
    background: string;
    surface: string;
    textPrimary: string;
    textSecondary: string;
    /** Readable text color on a `primary`-colored surface — "#000000" or "#FFFFFF" only. */
    onPrimary: "#000000" | "#FFFFFF";
    border: string;
  };
  radius: { button: number; card: number };
  spacing: { xs: number; sm: number; md: number; lg: number; xl: number };
}

// ── Mapped data-source item shapes ──────────────────────────────────────────────────────────
// Field names deliberately mirror `mobile/features/server-driven-ui/blocks/types.ts`'s
// `BannerItem`/`PromotionItem`/`ProductItem`/`StoreItem` — `AppPreviewPanel` maps the admin's own
// DTOs (`BannerDto`, `DiscountDto` joined to `CatalogProductDto`, `CatalogProductDto`,
// `LocationDto`) into these same shapes, the same way `resolveBlocks.ts` maps *its* consumer-side
// DTOs into them, so the two mappings stay directly comparable field-by-field.

export interface PreviewBannerItem {
  id: string;
  title: string;
  subtitle?: string;
  imageUrl?: string;
  /** Only used by `NewsListPreview` (mirrors mobile's `newsList` reusing banner data, see
   *  `resolveBlocks.ts`'s `newsList` case — `publishedAt: item.validUntil`). */
  validUntil?: string;
}

export interface PreviewPromotionItem {
  id: string;
  title: string;
  subtitle?: string;
  imageUrl?: string;
  badge?: string;
}

export interface PreviewProductItem {
  id: string;
  name: string;
  price: number;
  unit?: string;
  imageUrl?: string;
}

export interface PreviewStoreItem {
  id: string;
  name: string;
  address?: string;
}

export interface PreviewContext {
  tokens: PreviewTokens;
  banners: PreviewBannerItem[];
  promotions: PreviewPromotionItem[];
  catalog: PreviewProductItem[];
  locations: PreviewStoreItem[];
  registryByType: Map<MobileConfigBlockType, BlockDefinitionDto>;
  /**
   * TASK-565: fired exactly once per drag gesture (on pointerup, not per pointermove) by the 4
   * resizable block previews below, with the clamped final value. `undefined` renders those
   * blocks without a grab handle (read-only) — `AppPreviewPanel` always passes it today, this is
   * just what makes the handle opt-in at the type level.
   */
  onResizeCommit?: (blockId: string, propName: string, value: number) => void;
}

// ── Shared helpers (mirror resolveBlocks.ts's own — same fallback/clamp/mapping behavior) ─────

function positiveInt(value: unknown, fallback: number, maximum: number): number {
  return typeof value === "number" && Number.isInteger(value)
    ? Math.min(maximum, Math.max(1, value))
    : fallback;
}

function columns(value: unknown): 2 | 3 {
  return value === 3 ? 3 : 2;
}

function str(value: unknown, fallback = ""): string {
  return typeof value === "string" ? value : fallback;
}

function bool(value: unknown, fallback: boolean): boolean {
  return typeof value === "boolean" ? value : fallback;
}

function num(value: unknown, fallback: number): number {
  return typeof value === "number" && Number.isFinite(value) ? value : fallback;
}

function money(value: number): string {
  return value.toLocaleString("uk-UA", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** Matches `resolveBlocks.ts`'s `actionLabels` exactly — do not invent different labels. */
const ACTION_LABELS: Record<string, string> = {
  home: "Головна",
  promotions: "Акції",
  catalog: "Каталог",
  loyalty: "Картка",
  coupons: "Купони",
  stores: "Магазини",
  news: "Новини",
  profile: "Профіль",
};

// ── Small shared visual primitives ──────────────────────────────────────────────────────────

function ImagePlaceholder({ height, tokens }: { height: number; tokens: PreviewTokens }) {
  return <div style={{ width: "100%", height, background: tokens.colors.border }} />;
}

function PreviewImage({
  imageUrl,
  height,
  tokens,
}: {
  imageUrl?: string;
  height: number;
  tokens: PreviewTokens;
}) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [imageUrl]);
  if (!imageUrl || failed) return <ImagePlaceholder height={height} tokens={tokens} />;
  return (
    // eslint-disable-next-line @next/next/no-img-element -- external tenant/product-supplied URL, same opt-out as BannerForm.tsx/ThemeEditorSection.tsx.
    <img
      src={imageUrl}
      alt=""
      onError={() => setFailed(true)}
      style={{ width: "100%", height, objectFit: "cover", display: "block" }}
    />
  );
}

/** Small floating badge marking clearly-fabricated preview content (loyalty blocks) — an admin
 *  viewing the App Builder has no consumer session to read a real balance from (ADR-031). */
function SampleDataBadge() {
  const style: CSSProperties = {
    position: "absolute",
    top: 8,
    right: 8,
    background: "rgba(0,0,0,0.55)",
    color: "#FFFFFF",
    fontSize: 9,
    fontWeight: 700,
    letterSpacing: 0.3,
    padding: "2px 7px",
    borderRadius: 999,
    textTransform: "uppercase",
  };
  return <span style={style}>приклад даних</span>;
}

// ── heroBanner ───────────────────────────────────────────────────────────────────────────────

export function HeroBannerPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const title = str(block.props.title);
  const subtitle = str(block.props.subtitle) || undefined;
  const imageUrl = str(block.props.imageUrl) || undefined;
  const ctaLabel = str(block.props.ctaLabel) || undefined;
  const heightPx = num(block.props.heightPx, 190);
  const [imgFailed, setImgFailed] = useState(false);
  useEffect(() => setImgFailed(false), [imageUrl]);
  const showImage = !!imageUrl && !imgFailed;

  return (
    <div
      style={{
        position: "relative",
        minHeight: heightPx,
        overflow: "hidden",
        borderRadius: tokens.radius.card,
        background: tokens.colors.primary,
      }}
    >
      {showImage && (
        // eslint-disable-next-line @next/next/no-img-element
        <img
          src={imageUrl}
          alt=""
          onError={() => setImgFailed(true)}
          style={{ position: "absolute", inset: 0, width: "100%", height: "100%", objectFit: "cover" }}
        />
      )}
      <div
        style={{
          position: "relative",
          minHeight: heightPx,
          display: "flex",
          flexDirection: "column",
          justifyContent: "flex-end",
          padding: tokens.spacing.lg,
          background: showImage ? "rgba(0,0,0,0.4)" : "transparent",
          boxSizing: "border-box",
        }}
      >
        <span style={{ color: tokens.colors.onPrimary, fontSize: 26, fontWeight: 800 }}>{title}</span>
        {subtitle && (
          <span style={{ color: tokens.colors.onPrimary, fontSize: 14, marginTop: 6 }}>{subtitle}</span>
        )}
        {ctaLabel && (
          <span
            style={{
              alignSelf: "flex-start",
              marginTop: tokens.spacing.md,
              borderRadius: tokens.radius.button,
              background: tokens.colors.surface,
              color: tokens.colors.primary,
              fontWeight: 700,
              fontSize: 13,
              padding: `${tokens.spacing.sm}px ${tokens.spacing.md}px`,
            }}
          >
            {ctaLabel}
          </span>
        )}
      </div>
      <ResizeHandle axis="height" block={block} ctx={ctx} propName="heightPx" currentPx={heightPx} />
    </div>
  );
}

// ── bannerCarousel ───────────────────────────────────────────────────────────────────────────

export function BannerCarouselPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const limit = positiveInt(block.props.limit, 5, 10);
  const cardWidthPx = num(block.props.cardWidthPx, 280);
  const items = ctx.banners.slice(0, limit);

  return (
    <div style={{ position: "relative", display: "flex", overflowX: "auto", gap: tokens.spacing.sm, paddingBottom: 2 }}>
      {items.map((item) => (
        <div
          key={item.id}
          style={{
            width: cardWidthPx,
            flexShrink: 0,
            overflow: "hidden",
            borderRadius: tokens.radius.card,
            background: tokens.colors.surface,
          }}
        >
          <PreviewImage imageUrl={item.imageUrl} height={130} tokens={tokens} />
          <div style={{ padding: tokens.spacing.md }}>
            <div style={{ color: tokens.colors.textPrimary, fontSize: 17, fontWeight: 700 }}>{item.title}</div>
            {item.subtitle && (
              <div style={{ color: tokens.colors.textSecondary, marginTop: 4, fontSize: 13 }}>{item.subtitle}</div>
            )}
          </div>
        </div>
      ))}
      {items.length === 0 && <EmptyHint tokens={tokens} text="Немає активних банерів для показу." />}
      <ResizeHandle axis="width" block={block} ctx={ctx} propName="cardWidthPx" currentPx={cardWidthPx} />
    </div>
  );
}

// ── loyaltyCard / loyaltyBalance (sample data — see ADR-031) ────────────────────────────────

export function LoyaltyCardPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const showTier = bool(block.props.showTier, true);

  return (
    <div
      style={{
        position: "relative",
        borderRadius: tokens.radius.card,
        padding: tokens.spacing.lg,
        background: tokens.colors.primary,
      }}
    >
      <SampleDataBadge />
      <div style={{ color: tokens.colors.onPrimary, fontSize: 13 }}>Картка покупця</div>
      <div style={{ color: tokens.colors.onPrimary, fontSize: 32, fontWeight: 800, marginTop: 8 }}>{money(1250)}</div>
      <div style={{ display: "flex", justifyContent: "space-between", marginTop: tokens.spacing.lg }}>
        <span style={{ color: tokens.colors.onPrimary }}>•••• 4821</span>
        {showTier && <span style={{ color: tokens.colors.onPrimary, fontWeight: 700 }}>Срібний</span>}
      </div>
    </div>
  );
}

export function LoyaltyBalancePreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const showLabel = bool(block.props.showPointsLabel, true);

  return (
    <div
      style={{
        position: "relative",
        borderRadius: tokens.radius.card,
        padding: tokens.spacing.md,
        background: tokens.colors.surface,
      }}
    >
      <SampleDataBadge />
      {showLabel && <div style={{ color: tokens.colors.textSecondary }}>Бонусний баланс</div>}
      <div style={{ color: tokens.colors.textPrimary, fontSize: 25, fontWeight: 800, marginTop: 4 }}>
        {money(1250)} бонусів
      </div>
    </div>
  );
}

// ── promotionCarousel / promotionGrid ───────────────────────────────────────────────────────

function PromotionCard({ item, width, tokens }: { item: PreviewPromotionItem; width: number | string; tokens: PreviewTokens }) {
  return (
    <div style={{ width, flexShrink: 0, overflow: "hidden", borderRadius: tokens.radius.card, background: tokens.colors.surface }}>
      <PreviewImage imageUrl={item.imageUrl} height={120} tokens={tokens} />
      <div style={{ padding: tokens.spacing.md }}>
        {item.badge && (
          <div style={{ color: tokens.colors.primary, fontSize: 11, fontWeight: 800 }}>{item.badge}</div>
        )}
        <div style={{ color: tokens.colors.textPrimary, fontWeight: 700, marginTop: 3, fontSize: 13 }}>{item.title}</div>
        {item.subtitle && (
          <div style={{ color: tokens.colors.textSecondary, fontSize: 12, marginTop: 4 }}>{item.subtitle}</div>
        )}
      </div>
    </div>
  );
}

function SectionTitle({ title, tokens }: { title?: string; tokens: PreviewTokens }) {
  if (!title) return null;
  return (
    <div style={{ color: tokens.colors.textPrimary, fontSize: 20, fontWeight: 800, marginBottom: tokens.spacing.sm }}>
      {title}
    </div>
  );
}

function EmptyHint({ tokens, text }: { tokens: PreviewTokens; text: string }) {
  return <div style={{ color: tokens.colors.textSecondary, fontSize: 12, padding: tokens.spacing.sm }}>{text}</div>;
}

export function PromotionCarouselPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const title = str(block.props.title) || undefined;
  const limit = positiveInt(block.props.limit, 10, 30);
  const cardWidthPx = num(block.props.cardWidthPx, 210);
  const items = ctx.promotions.slice(0, limit);

  return (
    <div>
      <SectionTitle title={title} tokens={tokens} />
      <div style={{ position: "relative", display: "flex", overflowX: "auto", gap: tokens.spacing.sm, paddingBottom: 2 }}>
        {items.map((item) => (
          <PromotionCard key={item.id} item={item} width={cardWidthPx} tokens={tokens} />
        ))}
        {items.length === 0 && <EmptyHint tokens={tokens} text="Немає активних акцій для показу." />}
        <ResizeHandle axis="width" block={block} ctx={ctx} propName="cardWidthPx" currentPx={cardWidthPx} />
      </div>
    </div>
  );
}

export function PromotionGridPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const title = str(block.props.title) || undefined;
  const limit = positiveInt(block.props.limit, 12, 30);
  const width = columns(block.props.columns) === 3 ? "31%" : "48%";
  const items = ctx.promotions.slice(0, limit);

  return (
    <div>
      <SectionTitle title={title} tokens={tokens} />
      <div style={{ display: "flex", flexWrap: "wrap", gap: tokens.spacing.sm }}>
        {items.map((item) => (
          <PromotionCard key={item.id} item={item} width={width} tokens={tokens} />
        ))}
        {items.length === 0 && <EmptyHint tokens={tokens} text="Немає активних акцій для показу." />}
      </div>
    </div>
  );
}

// ── productCarousel / productGrid ───────────────────────────────────────────────────────────

function ProductCard({ item, width, tokens }: { item: PreviewProductItem; width: number | string; tokens: PreviewTokens }) {
  return (
    <div style={{ width, flexShrink: 0, overflow: "hidden", borderRadius: tokens.radius.card, background: tokens.colors.surface }}>
      <PreviewImage imageUrl={item.imageUrl} height={120} tokens={tokens} />
      <div style={{ padding: tokens.spacing.md }}>
        <div
          style={{
            color: tokens.colors.textPrimary,
            fontWeight: 700,
            fontSize: 13,
            overflow: "hidden",
            textOverflow: "ellipsis",
            display: "-webkit-box",
            WebkitLineClamp: 2,
            WebkitBoxOrient: "vertical",
          }}
        >
          {item.name}
        </div>
        <div style={{ color: tokens.colors.primary, fontSize: 17, fontWeight: 800, marginTop: 7 }}>
          {money(item.price)} ₴
        </div>
        {item.unit && <div style={{ color: tokens.colors.textSecondary, fontSize: 11, marginTop: 2 }}>{item.unit}</div>}
      </div>
    </div>
  );
}

export function ProductCarouselPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const title = str(block.props.title) || undefined;
  const limit = positiveInt(block.props.limit, 10, 30);
  const cardWidthPx = num(block.props.cardWidthPx, 170);
  const items = ctx.catalog.slice(0, limit);

  return (
    <div>
      <SectionTitle title={title} tokens={tokens} />
      <div style={{ position: "relative", display: "flex", overflowX: "auto", gap: tokens.spacing.sm, paddingBottom: 2 }}>
        {items.map((item) => (
          <ProductCard key={item.id} item={item} width={cardWidthPx} tokens={tokens} />
        ))}
        {items.length === 0 && <EmptyHint tokens={tokens} text="Немає товарів із роздрібною ціною для показу." />}
        <ResizeHandle axis="width" block={block} ctx={ctx} propName="cardWidthPx" currentPx={cardWidthPx} />
      </div>
    </div>
  );
}

export function ProductGridPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const title = str(block.props.title) || undefined;
  const limit = positiveInt(block.props.limit, 12, 30);
  const width = columns(block.props.columns) === 3 ? "31%" : "48%";
  const items = ctx.catalog.slice(0, limit);

  return (
    <div>
      <SectionTitle title={title} tokens={tokens} />
      <div style={{ display: "flex", flexWrap: "wrap", gap: tokens.spacing.sm }}>
        {items.map((item) => (
          <ProductCard key={item.id} item={item} width={width} tokens={tokens} />
        ))}
        {items.length === 0 && <EmptyHint tokens={tokens} text="Немає товарів із роздрібною ціною для показу." />}
      </div>
    </div>
  );
}

// ── sectionHeader ────────────────────────────────────────────────────────────────────────────

export function SectionHeaderPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const title = str(block.props.title);
  const subtitle = str(block.props.subtitle) || undefined;
  const alignment = block.props.alignment === "center" ? "center" : "left";
  const actionLabel = str(block.props.actionLabel) || undefined;

  return (
    <div style={{ display: "flex", alignItems: "flex-end" }}>
      <div style={{ flex: 1, textAlign: alignment }}>
        <div style={{ color: tokens.colors.textPrimary, fontSize: 21, fontWeight: 800 }}>{title}</div>
        {subtitle && <div style={{ color: tokens.colors.textSecondary, marginTop: 3, fontSize: 13 }}>{subtitle}</div>}
      </div>
      {actionLabel && <span style={{ color: tokens.colors.primary, fontWeight: 700, fontSize: 13 }}>{actionLabel}</span>}
    </div>
  );
}

// ── quickActions ─────────────────────────────────────────────────────────────────────────────

export function QuickActionsPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const actions = Array.isArray(block.props.actions)
    ? block.props.actions.filter((v): v is string => typeof v === "string")
    : [];

  return (
    <div style={{ display: "flex", flexWrap: "wrap", gap: tokens.spacing.sm }}>
      {actions.map((action) => (
        <div key={action} style={{ width: "23%", display: "flex", flexDirection: "column", alignItems: "center" }}>
          <div
            style={{
              width: 48,
              height: 48,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              borderRadius: tokens.radius.button,
              background: tokens.colors.border,
            }}
          >
            <span style={{ color: tokens.colors.primary, fontWeight: 800 }}>{action.slice(0, 1).toUpperCase()}</span>
          </div>
          <span
            style={{
              color: tokens.colors.textPrimary,
              fontSize: 12,
              textAlign: "center",
              marginTop: 6,
            }}
          >
            {ACTION_LABELS[action] ?? action}
          </span>
        </div>
      ))}
    </div>
  );
}

// ── newsList (reuses banner data — mirrors mobile's current interim behavior, ADR-031) ────────

export function NewsListPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const limit = positiveInt(block.props.limit, 5, 20);
  const items = ctx.banners.slice(0, limit);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: tokens.spacing.sm }}>
      {items.map((item) => (
        <div
          key={item.id}
          style={{ display: "flex", overflow: "hidden", borderRadius: tokens.radius.card, background: tokens.colors.surface }}
        >
          <div style={{ width: 96, flexShrink: 0 }}>
            <PreviewImage imageUrl={item.imageUrl} height={96} tokens={tokens} />
          </div>
          <div style={{ flex: 1, padding: tokens.spacing.md, minWidth: 0 }}>
            <div style={{ color: tokens.colors.textPrimary, fontWeight: 700, fontSize: 13 }}>{item.title}</div>
            {item.subtitle && (
              <div style={{ color: tokens.colors.textSecondary, fontSize: 12, marginTop: 4 }}>{item.subtitle}</div>
            )}
          </div>
        </div>
      ))}
      {items.length === 0 && <EmptyHint tokens={tokens} text="Немає активних банерів для показу." />}
    </div>
  );
}

// ── storeList ────────────────────────────────────────────────────────────────────────────────
// Static mirror of `StoreListBlock`'s collapsed (default) state — the real block's expand/select
// interaction is consumer-only behavior, out of scope for a read-only admin mock (ADR-031: "still
// deliberately approximate, not pixel-perfect").

export function StoreListPreview({ block, ctx }: { block: MobileConfigBlockInstance; ctx: PreviewContext }) {
  const { tokens } = ctx;
  const title = str(block.props.title) || "Магазини";
  const limit = positiveInt(block.props.limit, 10, 30);
  const items = ctx.locations.slice(0, limit);

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        padding: tokens.spacing.md,
        borderRadius: tokens.radius.card,
        background: tokens.colors.surface,
      }}
    >
      <div
        style={{
          width: 42,
          height: 42,
          flexShrink: 0,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          borderRadius: tokens.radius.button,
          background: tokens.colors.border,
          color: tokens.colors.primary,
          fontWeight: 800,
        }}
      >
        📍
      </div>
      <div style={{ flex: 1, marginLeft: tokens.spacing.sm, minWidth: 0 }}>
        <div style={{ color: tokens.colors.textPrimary, fontSize: 17, fontWeight: 800 }}>{title}</div>
        <div style={{ color: tokens.colors.textSecondary, fontSize: 12, marginTop: 2 }}>
          {items.length ? `${items.length} доступних` : "Немає доступних магазинів"}
        </div>
      </div>
    </div>
  );
}

// ── Resize handle (TASK-565) ────────────────────────────────────────────────────────────────
// Only rendered for the 4 block types with a real, currently-hardcoded visual dimension
// (heroBanner.heightPx, and cardWidthPx on the 3 carousel variants — NOT the grid variants,
// which already have a `columns` prop instead). Reads min/max from `ctx.registryByType`, the
// same registry the Property Editor's own number-field bounds come from.

function ResizeHandle({
  axis,
  block,
  ctx,
  propName,
  currentPx,
}: {
  axis: "width" | "height";
  block: MobileConfigBlockInstance;
  ctx: PreviewContext;
  propName: "heightPx" | "cardWidthPx";
  currentPx: number;
}) {
  const def = ctx.registryByType.get(block.type)?.validationSchema.find((d) => d.name === propName);
  const min = def?.min ?? (axis === "height" ? 120 : 120);
  const max = def?.max ?? (axis === "height" ? 260 : 360);
  const { value, dragging, handleProps } = useResizeDrag({
    axis,
    value: currentPx,
    min,
    max,
    onCommit: (v) => ctx.onResizeCommit?.(block.id, propName, v),
  });

  if (!ctx.onResizeCommit) return null;

  const style: CSSProperties =
    axis === "height"
      ? {
          position: "absolute",
          left: "50%",
          bottom: 2,
          transform: "translateX(-50%)",
          width: 40,
          height: 6,
          borderRadius: 3,
          cursor: "ns-resize",
          background: dragging ? "#3B82F6" : "rgba(255,255,255,0.85)",
          boxShadow: "0 1px 3px rgba(0,0,0,0.4)",
        }
      : {
          position: "absolute",
          top: "50%",
          right: -3,
          transform: "translateY(-50%)",
          width: 6,
          height: 40,
          borderRadius: 3,
          cursor: "ew-resize",
          background: dragging ? "#3B82F6" : "rgba(255,255,255,0.85)",
          boxShadow: "0 1px 3px rgba(0,0,0,0.4)",
          zIndex: 5,
        };

  return (
    <div
      {...handleProps}
      title={`${propName === "heightPx" ? "Висота" : "Ширина картки"}: ${Math.round(value)}px`}
      style={style}
    />
  );
}

// ── Dispatch ─────────────────────────────────────────────────────────────────────────────────

const PREVIEW_COMPONENTS: Record<
  MobileConfigBlockType,
  (props: { block: MobileConfigBlockInstance; ctx: PreviewContext }) => JSX.Element
> = {
  heroBanner: HeroBannerPreview,
  bannerCarousel: BannerCarouselPreview,
  loyaltyCard: LoyaltyCardPreview,
  loyaltyBalance: LoyaltyBalancePreview,
  promotionCarousel: PromotionCarouselPreview,
  promotionGrid: PromotionGridPreview,
  productCarousel: ProductCarouselPreview,
  productGrid: ProductGridPreview,
  sectionHeader: SectionHeaderPreview,
  quickActions: QuickActionsPreview,
  newsList: NewsListPreview,
  storeList: StoreListPreview,
};

export function renderBlockPreview(block: MobileConfigBlockInstance, ctx: PreviewContext) {
  const Component = PREVIEW_COMPONENTS[block.type];
  if (!Component) return null;
  return <Component key={block.id} block={block} ctx={ctx} />;
}
