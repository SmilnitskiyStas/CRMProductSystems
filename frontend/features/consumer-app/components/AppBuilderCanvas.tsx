"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import {
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragStartEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  arrayMove,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  Blocks,
  CreditCard,
  GalleryHorizontal,
  Grid3x3,
  GripVertical,
  Heading,
  Image as ImageIcon,
  Info,
  LayoutGrid,
  MapPin,
  Newspaper,
  Pencil,
  Percent,
  ShoppingBag,
  Trash2,
  Wallet,
  Zap,
  type LucideIcon,
} from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { extractDraftValidationErrors } from "../api/mobileConfigDraft";
import { AppPreviewPanel } from "./AppPreviewPanel";
import { BlockPropertyEditor } from "./BlockPropertyEditor";
import { useBlockRegistry } from "../hooks/useBlockRegistry";
import { useMediaQuery } from "../hooks/useMediaQuery";
import { useMobileConfigDraft, useSaveMobileConfigDraft } from "../hooks/useMobileConfigDraft";
import { useUnsavedChangesGuard } from "../hooks/useUnsavedChangesGuard";
import {
  MOBILE_CONFIG_CURRENT_SCHEMA_VERSION,
  MOBILE_CONFIG_FEATURE_KEYS,
  MOBILE_CONFIG_PAGE_NAMES,
  type BlockDefinitionDto,
  type MobileConfigBlockInstance,
  type MobileConfigBlockType,
  type MobileConfigDocument,
  type MobileConfigFeatures,
  type MobileConfigPage,
  type MobileConfigPageName,
} from "../types";

// ── Style constants (mirrors ThemeEditorSection.tsx's conventions — this feature area has no
// shadcn form primitives of its own) ────────────────────────────────────────────────────────

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
  margin: "0 0 10px",
};

// ── Icon lookup (decorative only) ───────────────────────────────────────────────────────────
// BlockRegistry.cs's `icon` values are free-text hints, not guaranteed lucide-react export
// names (e.g. "images" has no plural icon in this lucide version) — mapped by hand for the 12
// Core Blocks V1 icons that exist today, with a generic fallback so a future registry addition
// renders instead of crashing.

const BLOCK_ICONS: Record<string, LucideIcon> = {
  image: ImageIcon,
  images: GalleryHorizontal,
  "credit-card": CreditCard,
  wallet: Wallet,
  percent: Percent,
  "layout-grid": LayoutGrid,
  "shopping-bag": ShoppingBag,
  "grid-3x3": Grid3x3,
  heading: Heading,
  zap: Zap,
  newspaper: Newspaper,
  "map-pin": MapPin,
};
const DEFAULT_BLOCK_ICON: LucideIcon = Blocks;

function iconFor(name: string): LucideIcon {
  return BLOCK_ICONS[name] ?? DEFAULT_BLOCK_ICON;
}

// CLAUDE CODE SPEC §11's palette grouping order — BlockCategories.cs constants, in display order.
const CATEGORY_ORDER = ["banner", "loyalty", "promotions", "products", "news", "stores", "layout"] as const;

// ── Seed document (TASK-539 default-seeding decision) ──────────────────────────────────────
// A brand-new tenant's first App Builder visit has no draft yet (`hasDraft: false`). Rather than
// let the first save fail confusingly on unrelated `features`/`navigation` whitelist rules
// (MobileConfigWhitelists.cs) the user never touched, seed a minimal valid starting document:
// every feature flag defaulted to false (Stage D's Feature Flags UI is what actually turns them
// on), and the required-minimum-2 navigation items using the exact home/profile label+icon
// convention already established by `mobile/features/mobile-config/mock.ts` and the backend's own
// test fixtures (MobileConfigValidatorTests, MobileConfigPublishedReadServiceTests) — not invented
// here. TASK-542 (Navigation Builder) is expected to build on this default rather than replace it
// silently.

function buildSeedDocument(): MobileConfigDocument {
  const features = Object.fromEntries(
    MOBILE_CONFIG_FEATURE_KEYS.map((key) => [key, false]),
  ) as MobileConfigFeatures;

  // TASK-541: seed every whitelisted page (not just Home) with an empty block list — Promotions/
  // Catalog/News are scaffolds a tenant builds up via the same canvas mechanism, matching Home.
  const pages = {} as Record<MobileConfigPageName, MobileConfigPage>;
  for (const page of MOBILE_CONFIG_PAGE_NAMES) {
    pages[page] = { blocks: [] };
  }

  return {
    schemaVersion: MOBILE_CONFIG_CURRENT_SCHEMA_VERSION,
    features,
    navigation: [
      { type: "home", label: "Головна", icon: "home" },
      { type: "profile", label: "Профіль", icon: "user" },
    ],
    pages,
  };
}

/** Defensive normalization for a document loaded from the server — every whitelisted page
 *  (TASK-541: Home/Promotions/Catalog/News) always gets a `blocks` array (sorted by `order`), so
 *  the rest of this component never has to null-check `pages[page]` regardless of which page is
 *  active. A document saved before TASK-541 (Home-only) normalizes cleanly: the missing pages get
 *  an empty scaffold, Home's existing blocks pass through unchanged. */
function normalizeDocument(doc: MobileConfigDocument): MobileConfigDocument {
  const pages = {} as Record<MobileConfigPageName, MobileConfigPage>;
  for (const page of MOBILE_CONFIG_PAGE_NAMES) {
    const blocks = [...(doc.pages[page]?.blocks ?? [])].sort((a, b) => a.order - b.order);
    pages[page] = { blocks };
  }
  return { ...doc, pages };
}

/** TASK-541: generalized from TASK-539's `withHomeBlocks` — same read-modify-write shape, but
 *  parameterized by which page is currently active so Promotions/Catalog/News reuse the identical
 *  mechanism Home already had. Only `pages[page].blocks` is ever touched; every other page and
 *  every other top-level key passes through untouched. */
function withPageBlocks(
  doc: MobileConfigDocument,
  page: MobileConfigPageName,
  updater: (blocks: MobileConfigBlockInstance[]) => MobileConfigBlockInstance[],
): MobileConfigDocument {
  const current = doc.pages[page]?.blocks ?? [];
  const next = updater(current).map((block, index) => ({ ...block, order: index }));
  return { ...doc, pages: { ...doc.pages, [page]: { blocks: next } } };
}

type DragGhost =
  | { kind: "palette"; def: BlockDefinitionDto }
  | { kind: "canvas"; block: MobileConfigBlockInstance; def: BlockDefinitionDto | undefined };

/**
 * TASK-539/541: App Builder foundation — drag & drop canvas for a page's block list
 * (`pages[activePage].blocks`). Fills the `/consumer-app/pages` placeholder TASK-535 scaffolded.
 * TASK-541 generalized this from Home-only to all four whitelisted pages (Home/Promotions/
 * Catalog/News) via a page-tab selector — the mechanism (registry palette, drag-to-add/reorder,
 * remove, property edit, save) is identical for every page, only which page's block array is
 * being read/written changes.
 *
 * READ-MODIFY-WRITE (TASK-538b's PUT takes the *whole* config document): this component keeps
 * the entire parsed `MobileConfigDocument` in state, not just one page's block array — every add/
 * remove/reorder mutates only `document.pages[activePage].blocks` via `withPageBlocks`, and Save
 * `JSON.stringify`s the full, still-valid document back. `features`/`navigation`/every other page
 * are therefore carried through untouched on every save, never clobbered — switching the active
 * page tab never loses unsaved edits made on another page, since they all live in the same
 * `configDoc` state until Save.
 *
 * No publish action anywhere in this component by design (TASK-539 DoD) — publishing lives on
 * `/consumer-app/versions` (TASK-546); the draft notice below links there.
 */
export function AppBuilderCanvas() {
  const t = useTranslations("Dashboard.consumerApp.appBuilder");
  const registryQuery = useBlockRegistry();
  const draftQuery = useMobileConfigDraft();
  const save = useSaveMobileConfigDraft();

  // TASK-567: the palette/canvas/preview row below only fits on one line (`flexWrap: "nowrap"`)
  // above this breakpoint — chosen with headroom over the row's combined flex-basis (300 palette +
  // 420 canvas + 500 preview + 2×20 gap = 1260px) so real dashboard chrome (sidebar nav + content
  // padding, which eats into the raw browser width) doesn't tip it back into a forced wrap right at
  // the edge. Below it, `flexWrap: "wrap"` drops the preview column onto its own line instead of
  // clipping/overflowing — verified empirically against this repo's dashboard shell, see task log.
  const canFitThreeColumns = useMediaQuery("(min-width: 1360px)");

  const [configDoc, setConfigDoc] = useState<MobileConfigDocument | null>(null);
  const [dirty, setDirty] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [activeDrag, setActiveDrag] = useState<DragGhost | null>(null);
  // TASK-541: which page's block list the palette/canvas below is currently editing.
  const [activePage, setActivePage] = useState<MobileConfigPageName>("home");
  // TASK-540: id of the block whose Property Editor drawer is open, or null when closed.
  const [selectedBlockId, setSelectedBlockId] = useState<string | null>(null);
  // TASK-565: the open drawer's unapplied, live-watched form values (BlockPropertyEditor's own
  // `watch()`) — feeds `previewBlocks` below so the preview panel reflects every keystroke before
  // "Apply". Reset to `null` whenever the selection changes (new block, or drawer closed) so a
  // stale live value never leaks onto a different block.
  const [liveProps, setLiveProps] = useState<Record<string, unknown> | null>(null);
  // TASK-568: show/hide toggle for the whole preview column — pure local UI state, no persistence.
  // Default `true` preserves today's always-visible behavior.
  const [previewVisible, setPreviewVisible] = useState(true);

  // TASK-546: warns before losing unsaved edits (tab-close/refresh always; in-app link-click
  // navigation best-effort) — see useUnsavedChangesGuard.ts's remarks for exact coverage.
  useUnsavedChangesGuard(dirty, t("unsavedChangesWarning"));

  // Hydrate local state from the server — brand-new tenant gets the seed document instead of a
  // fabricated draft (see buildSeedDocument's remarks above). Re-runs whenever draftQuery.data's
  // reference changes, including right after a successful save (same convention
  // ThemeEditorSection's `reset` effect uses to re-sync from the server's canonical response).
  useEffect(() => {
    if (!draftQuery.data) return;
    const next =
      draftQuery.data.hasDraft && draftQuery.data.configurationJson
        ? normalizeDocument(JSON.parse(draftQuery.data.configurationJson) as MobileConfigDocument)
        : buildSeedDocument();
    setConfigDoc(next);
    setDirty(false);
  }, [draftQuery.data]);

  const blocks = configDoc?.pages[activePage]?.blocks ?? [];
  const selectedBlock = selectedBlockId ? blocks.find((b) => b.id === selectedBlockId) ?? null : null;

  // TASK-565: a stale live-watched value must never leak onto a different block (or linger after
  // the drawer closes) — reset on every selection change, covering every `setSelectedBlockId` call
  // site (edit-click, close, remove, page switch, Apply) in one place rather than each of them.
  useEffect(() => {
    setLiveProps(null);
  }, [selectedBlockId]);

  // TASK-565: the preview panel reads this instead of raw `blocks` — substitutes the selected
  // block's props with its unapplied live-watched values (if the drawer is open and has emitted
  // at least one `onLiveChange`) so typing in the Property Editor reflects instantly, before
  // "Apply" ever touches `configDoc`.
  const previewBlocks = useMemo(
    () =>
      liveProps && selectedBlockId
        ? blocks.map((b) => (b.id === selectedBlockId ? { ...b, props: liveProps } : b))
        : blocks,
    [blocks, selectedBlockId, liveProps],
  );

  // TASK-568: `AppPreviewPanel` now takes every page's blocks (its own bottom-nav mockup lets the
  // admin browse pages independent of `activePage`) — substitute just `activePage`'s entry with
  // `previewBlocks` (the live-edit-merged array above) so TASK-565's before-Apply live editing
  // still reflects instantly whenever the preview's own nav happens to be showing that same page
  // (the common case: preview defaults to `activePage` until the admin clicks elsewhere in the
  // mockup's nav). Every other page passes through `configDoc.pages` untouched — they never have
  // an open drawer, so there's nothing to live-merge for them.
  const previewPages = useMemo(
    () => ({ ...configDoc?.pages, [activePage]: { blocks: previewBlocks } }),
    [configDoc, activePage, previewBlocks],
  );

  // TASK-541: switching pages closes any open Property Editor — a selected block id only makes
  // sense for the page it was selected on (block ids never collide across pages, so `selectedBlock`
  // above would already resolve to `null` on another page; this just makes the intent explicit and
  // avoids a stale drawer flashing open for a split second on tab switch).
  function handlePageChange(page: MobileConfigPageName) {
    setActivePage(page);
    setSelectedBlockId(null);
  }

  const registryByType = useMemo(() => {
    const map = new Map<MobileConfigBlockType, BlockDefinitionDto>();
    for (const def of registryQuery.data ?? []) map.set(def.type, def);
    return map;
  }, [registryQuery.data]);

  const categorized = useMemo(() => {
    const map = new Map<string, BlockDefinitionDto[]>();
    for (const def of registryQuery.data ?? []) {
      const list = map.get(def.category) ?? [];
      list.push(def);
      map.set(def.category, list);
    }
    return map;
  }, [registryQuery.data]);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  function addBlockAt(type: MobileConfigBlockType, index: number | undefined) {
    const def = registryByType.get(type);
    if (!def) return;
    setConfigDoc((doc) => {
      if (!doc) return doc;
      return withPageBlocks(doc, activePage, (current) => {
        const newBlock: MobileConfigBlockInstance = {
          id: crypto.randomUUID(),
          type,
          order: 0,
          props: { ...def.defaultProps },
        };
        const insertAt = index === undefined || index < 0 || index > current.length ? current.length : index;
        const next = [...current];
        next.splice(insertAt, 0, newBlock);
        return next;
      });
    });
    setDirty(true);
    setSaveError(null);
  }

  function removeBlock(id: string) {
    setConfigDoc((doc) =>
      doc ? withPageBlocks(doc, activePage, (current) => current.filter((b) => b.id !== id)) : doc,
    );
    setDirty(true);
    setSaveError(null);
    setSelectedBlockId((current) => (current === id ? null : current));
  }

  // TASK-540: Property Editor "Apply" writes the edited `props` back into this block instance
  // only — never touches the backend. The canvas's own "Save draft" button (below) is still the
  // only thing that persists anything, matching this feature's explicit-save convention.
  function updateBlockProps(id: string, props: Record<string, unknown>) {
    setConfigDoc((doc) =>
      doc
        ? withPageBlocks(doc, activePage, (current) => current.map((b) => (b.id === id ? { ...b, props } : b)))
        : doc,
    );
    setDirty(true);
    setSaveError(null);
  }

  // TASK-565: single-field commit for the preview panel's resize drag handles — same
  // read-modify-write shape as `updateBlockProps` above, but merges one prop key instead of
  // replacing the whole `props` object, and is called by `useResizeDrag` exactly once per drag
  // gesture (on pointerup), never on every pointermove — `dirty` must not churn mid-drag.
  function updateBlockSizeProp(id: string, propName: string, value: number) {
    setConfigDoc((doc) =>
      doc
        ? withPageBlocks(doc, activePage, (current) =>
            current.map((b) => (b.id === id ? { ...b, props: { ...b.props, [propName]: value } } : b)),
          )
        : doc,
    );
    setDirty(true);
    setSaveError(null);
  }

  function handleDragStart(event: DragStartEvent) {
    const data = event.active.data.current as
      | { source?: "palette"; blockType?: MobileConfigBlockType }
      | undefined;
    if (data?.source === "palette" && data.blockType) {
      const def = registryByType.get(data.blockType);
      if (def) setActiveDrag({ kind: "palette", def });
      return;
    }
    const block = blocks.find((b) => b.id === event.active.id);
    if (block) setActiveDrag({ kind: "canvas", block, def: registryByType.get(block.type) });
  }

  function handleDragEnd(event: DragEndEvent) {
    setActiveDrag(null);
    const { active, over } = event;
    if (!over) return;

    const data = active.data.current as { source?: "palette"; blockType?: MobileConfigBlockType } | undefined;
    if (data?.source === "palette" && data.blockType) {
      const overIndex = blocks.findIndex((b) => b.id === over.id);
      addBlockAt(data.blockType, overIndex === -1 ? blocks.length : overIndex);
      return;
    }

    if (active.id === over.id) return;
    setConfigDoc((doc) => {
      if (!doc) return doc;
      return withPageBlocks(doc, activePage, (current) => {
        const oldIndex = current.findIndex((b) => b.id === active.id);
        const newIndex = current.findIndex((b) => b.id === over.id);
        if (oldIndex === -1 || newIndex === -1) return current;
        return arrayMove(current, oldIndex, newIndex);
      });
    });
    setDirty(true);
    setSaveError(null);
  }

  async function handleSave() {
    if (!configDoc || save.isPending) return;
    setSaveError(null);
    try {
      await save.mutateAsync({ configurationJson: JSON.stringify(configDoc) });
      toast.success(t("saveSuccess"));
    } catch (err) {
      const fieldErrors = extractDraftValidationErrors(err);
      if (fieldErrors) {
        setSaveError(fieldErrors.map((e) => `${e.field}: ${e.message}`).join(" "));
      } else {
        setSaveError(err instanceof Error ? err.message : t("saveError"));
      }
    }
  }

  if (registryQuery.isLoading || draftQuery.isLoading || !configDoc) {
    return <div style={{ ...cardStyle, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>;
  }
  if (registryQuery.isError || draftQuery.isError) {
    return <div style={{ ...cardStyle, color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
      {/* TASK-541: page selector — switches which page's block list (`pages[activePage].blocks`)
          the palette/canvas/property-editor below operate on. Same document stays in state across
          tab switches, so unsaved edits on another page are never lost (see component JSDoc). */}
      <PageTabs activePage={activePage} onChange={handlePageChange} t={t} />

      <DndContext
        sensors={sensors}
        collisionDetection={closestCenter}
        onDragStart={handleDragStart}
        onDragEnd={handleDragEnd}
        onDragCancel={() => setActiveDrag(null)}
      >
        <div
          style={{
            display: "flex",
            gap: 20,
            alignItems: "flex-start",
            flexWrap: canFitThreeColumns ? "nowrap" : "wrap",
          }}
        >
            {/* Palette */}
            <div style={{ ...cardStyle, flex: "0 1 300px", position: "sticky", top: 20 }}>
              <p style={sectionLabelStyle}>{t("paletteTitle")}</p>
              <p style={{ color: "#4B5563", fontSize: 12, margin: "0 0 16px" }}>{t("paletteHint")}</p>
              {CATEGORY_ORDER.filter((category) => categorized.has(category)).map((category) => (
                <div key={category} style={{ marginBottom: 16 }}>
                  <p style={{ color: "#6B7280", fontSize: 11, fontWeight: 600, margin: "0 0 8px" }}>
                    {t(`categories.${category}`)}
                  </p>
                  <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                    {(categorized.get(category) ?? []).map((def) => (
                      <PaletteItem key={def.type} def={def} onAdd={() => addBlockAt(def.type, undefined)} />
                    ))}
                  </div>
                </div>
              ))}
            </div>

            {/* Canvas */}
            <div style={{ flex: "1 1 420px", minWidth: 0 }}>
              <div style={cardStyle}>
                <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 8, marginBottom: 10 }}>
                  {/* TASK-541: title reflects the active page tab, generalized from the old static
                      "Home screen" text — still reads exactly the same when Home is selected. */}
                  <p style={{ ...sectionLabelStyle, margin: 0 }}>{t("canvasTitle", { page: t(`pageTabs.${activePage}`) })}</p>
                  {/* TASK-568: show/hide toggle for the whole preview column — pure local UI
                      state, reclaims the canvas column's width when hidden (`flex: "1 1 420px"`
                      grows to fill the freed space once the preview column stops rendering). */}
                  <Btn variant="ghost" size="sm" onClick={() => setPreviewVisible((visible) => !visible)}>
                    {previewVisible ? t("hidePreviewButton") : t("showPreviewButton")}
                  </Btn>
                </div>
                <CanvasDropzone
                  blocks={blocks}
                  registryByType={registryByType}
                  onRemove={removeBlock}
                  onEdit={setSelectedBlockId}
                  t={t}
                />
              </div>

              {/* Draft-only notice — no publish/preview exists on this screen (TASK-539 DoD) */}
              <div
                style={{
                  display: "flex",
                  alignItems: "flex-start",
                  gap: 8,
                  marginTop: 16,
                  padding: "10px 12px",
                  background: "#1F2937",
                  borderRadius: 8,
                }}
              >
                <Info size={15} style={{ color: "#3B82F6", flexShrink: 0, marginTop: 1 }} />
                <p style={{ color: "#D1D5DB", fontSize: 12, margin: 0 }}>
                  {t("draftNotice")}{" "}
                  <Link href="/consumer-app/versions" style={{ color: "#93C5FD" }}>
                    {t("goToVersionsLink")}
                  </Link>
                </p>
              </div>

              {saveError && <p style={{ color: "#F87171", fontSize: 12, marginTop: 12 }}>{saveError}</p>}

              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 12,
                  marginTop: 16,
                  paddingTop: 16,
                  borderTop: "1px solid #1F2937",
                }}
              >
                <Btn onClick={handleSave} disabled={save.isPending || !dirty}>
                  {save.isPending ? t("savingButton") : t("saveButton")}
                </Btn>
                {!dirty && <span style={{ color: "#4B5563", fontSize: 12 }}>{t("noChanges")}</span>}
              </div>
            </div>

            {/* TASK-564/565: live preview column — reads `previewPages` (TASK-568: every page's
                blocks, with `activePage`'s entry substituted by the open drawer's unapplied live
                edits, TASK-565) so add/remove/reorder, Apply, in-drawer typing, and resize-drag
                handles all reflect here within the same render. Known, accepted tradeoff:
                `BlockPropertyEditor`'s `DetailDrawer` is a fixed right-edge overlay (up to 520px)
                that overlaps part of this column on narrower viewports while open — not fixed
                here, see TASK-560's log.
                TASK-567: widened 340 → 500px so the column comfortably fits the widest device
                preset (Pixel 8 Pro, 448px) plus its frame's 8px border ×2 and breathing room — the
                column's own width stays fixed regardless of which preset is selected; the actual
                device frame (384–448px wide across presets) is centered within it via
                `PhoneFrame`'s own `margin: "0 auto"`, so switching devices never jumps the
                3-column layout.
                TASK-568: only rendered while `previewVisible` — when hidden, the canvas column
                (`flex: "1 1 420px"`, flex-grow 1) naturally reclaims the freed width. */}
            {previewVisible && (
              <div style={{ flex: "0 1 500px", position: "sticky", top: 20 }}>
                <AppPreviewPanel
                  pages={previewPages}
                  navigation={configDoc.navigation}
                  activePage={activePage}
                  registryByType={registryByType}
                  onResizeCommit={updateBlockSizeProp}
                />
              </div>
            )}
        </div>

        <DragOverlay>
          {activeDrag && <DragGhostCard ghost={activeDrag} />}
        </DragOverlay>

        {/* TASK-540: Property Editor drawer for the selected block. Keyed by block id so switching
            the selection remounts the form with fresh defaultValues instead of needing a manual
            reset effect. */}
        {selectedBlock && (
          <BlockPropertyEditor
            key={selectedBlock.id}
            block={selectedBlock}
            definition={registryByType.get(selectedBlock.type)}
            onClose={() => setSelectedBlockId(null)}
            onApply={(props) => {
              updateBlockProps(selectedBlock.id, props);
              setSelectedBlockId(null);
            }}
            onLiveChange={setLiveProps}
          />
        )}
      </DndContext>
    </div>
  );
}

// ── Page selector (TASK-541) ────────────────────────────────────────────────────────────────
// Underline-tab visual style, matching this feature area's existing `LifecycleTabs.tsx`
// convention (BannersSection/PromoProductsSection) — kept as a local component rather than
// reusing `LifecycleTabs` directly, since that component's type is hardcoded to its own
// `LifecycleTab` union and isn't shared infrastructure.

function PageTabs({
  activePage,
  onChange,
  t,
}: {
  activePage: MobileConfigPageName;
  onChange: (page: MobileConfigPageName) => void;
  t: ReturnType<typeof useTranslations>;
}) {
  return (
    <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937" }}>
      {MOBILE_CONFIG_PAGE_NAMES.map((page) => {
        const active = page === activePage;
        return (
          <button
            key={page}
            type="button"
            onClick={() => onChange(page)}
            style={{
              padding: "8px 16px",
              background: "transparent",
              border: "none",
              borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
              color: active ? "#3B82F6" : "#6B7280",
              fontSize: 13,
              fontWeight: active ? 600 : 400,
              cursor: "pointer",
              marginBottom: -1,
              transition: "color 0.15s",
            }}
          >
            {t(`pageTabs.${page}`)}
          </button>
        );
      })}
    </div>
  );
}

// ── Palette item (draggable + click-to-add for keyboard/pointer parity) ────────────────────

function PaletteItem({ def, onAdd }: { def: BlockDefinitionDto; onAdd: () => void }) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: `palette:${def.type}`,
    data: { source: "palette", blockType: def.type },
  });
  const Icon = iconFor(def.icon);

  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      style={{
        display: "flex",
        alignItems: "center",
        gap: 10,
        padding: "8px 10px",
        background: "#161B26",
        border: "1px solid #1F2937",
        borderRadius: 8,
        cursor: isDragging ? "grabbing" : "grab",
        opacity: isDragging ? 0.4 : 1,
        touchAction: "none",
        userSelect: "none",
      }}
      title={def.supportedDataSource}
    >
      <Icon size={16} style={{ color: "#9CA3AF", flexShrink: 0 }} />
      <span style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 500, flex: 1 }}>{def.displayName}</span>
      <button
        type="button"
        onClick={(e) => {
          e.stopPropagation();
          onAdd();
        }}
        aria-label={def.displayName}
        style={{
          width: 22,
          height: 22,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "transparent",
          border: "1px solid #374151",
          borderRadius: 6,
          color: "#9CA3AF",
          fontSize: 14,
          cursor: "pointer",
          flexShrink: 0,
        }}
      >
        +
      </button>
    </div>
  );
}

// ── Canvas dropzone + sortable list ─────────────────────────────────────────────────────────

function CanvasDropzone({
  blocks,
  registryByType,
  onRemove,
  onEdit,
  t,
}: {
  blocks: MobileConfigBlockInstance[];
  registryByType: Map<MobileConfigBlockType, BlockDefinitionDto>;
  onRemove: (id: string) => void;
  onEdit: (id: string) => void;
  t: ReturnType<typeof useTranslations>;
}) {
  const { setNodeRef } = useDroppable({ id: "canvas-dropzone" });

  return (
    <div
      ref={setNodeRef}
      style={{
        minHeight: 220,
        display: "flex",
        flexDirection: "column",
        gap: 8,
        padding: blocks.length === 0 ? 0 : 2,
      }}
    >
      <SortableContext items={blocks.map((b) => b.id)} strategy={verticalListSortingStrategy}>
        {blocks.map((block, index) => (
          <CanvasBlockCard
            key={block.id}
            block={block}
            index={index}
            def={registryByType.get(block.type)}
            onRemove={() => onRemove(block.id)}
            onEdit={() => onEdit(block.id)}
          />
        ))}
      </SortableContext>
      {blocks.length === 0 && (
        <div
          style={{
            flex: 1,
            minHeight: 220,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            textAlign: "center",
            color: "#4B5563",
            fontSize: 13,
            border: "1px dashed #2A3441",
            borderRadius: 10,
            padding: 24,
          }}
        >
          {t("emptyCanvas")}
        </div>
      )}
    </div>
  );
}

function CanvasBlockCard({
  block,
  index,
  def,
  onRemove,
  onEdit,
}: {
  block: MobileConfigBlockInstance;
  index: number;
  def: BlockDefinitionDto | undefined;
  onRemove: () => void;
  onEdit: () => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: block.id,
  });
  const Icon = def ? iconFor(def.icon) : Blocks;

  return (
    <div
      ref={setNodeRef}
      style={{
        display: "flex",
        alignItems: "center",
        gap: 10,
        padding: "10px 12px",
        background: "#161B26",
        border: "1px solid #1F2937",
        borderRadius: 8,
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
        zIndex: isDragging ? 10 : undefined,
      }}
    >
      <span
        {...attributes}
        {...listeners}
        style={{
          display: "flex",
          alignItems: "center",
          cursor: isDragging ? "grabbing" : "grab",
          color: "#4B5563",
          touchAction: "none",
        }}
      >
        <GripVertical size={16} />
      </span>
      <span
        style={{
          width: 20,
          height: 20,
          borderRadius: 6,
          background: "#0B0E14",
          color: "#6B7280",
          fontSize: 10,
          fontWeight: 700,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          flexShrink: 0,
        }}
      >
        {index + 1}
      </span>
      <Icon size={16} style={{ color: "#9CA3AF", flexShrink: 0 }} />
      <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500, flex: 1 }}>
        {def?.displayName ?? block.type}
      </span>
      <button
        type="button"
        onClick={onEdit}
        aria-label="edit"
        style={{
          width: 26,
          height: 26,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "transparent",
          border: "1px solid #374151",
          borderRadius: 6,
          color: "#9CA3AF",
          cursor: "pointer",
          flexShrink: 0,
        }}
      >
        <Pencil size={13} />
      </button>
      <button
        type="button"
        onClick={onRemove}
        aria-label="remove"
        style={{
          width: 26,
          height: 26,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "transparent",
          border: "1px solid #374151",
          borderRadius: 6,
          color: "#9CA3AF",
          cursor: "pointer",
          flexShrink: 0,
        }}
      >
        <Trash2 size={14} />
      </button>
    </div>
  );
}

function DragGhostCard({ ghost }: { ghost: DragGhost }) {
  const def = ghost.def;
  const Icon = def ? iconFor(def.icon) : Blocks;
  const label = def?.displayName ?? (ghost.kind === "canvas" ? ghost.block.type : "");

  return (
    <div
      style={{
        display: "flex",
        alignItems: "center",
        gap: 10,
        padding: "10px 12px",
        background: "#1F2937",
        border: "1px solid #3B82F6",
        borderRadius: 8,
        boxShadow: "0 8px 20px rgba(0,0,0,0.4)",
        cursor: "grabbing",
      }}
    >
      <Icon size={16} style={{ color: "#93C5FD", flexShrink: 0 }} />
      <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>{label}</span>
    </div>
  );
}
