"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useForm, useFieldArray, useWatch, type Control, type Path } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  GripVertical,
  Grid3x3,
  Home,
  Info,
  Map as MapIcon,
  Newspaper,
  Plus,
  QrCode,
  Tag,
  Ticket,
  Trash2,
  User,
  type LucideIcon,
} from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { extractDraftValidationErrors } from "../api/mobileConfigDraft";
import { useMobileConfigDraft, useSaveMobileConfigDraft } from "../hooks/useMobileConfigDraft";
import { useUnsavedChangesGuard } from "../hooks/useUnsavedChangesGuard";
import {
  MOBILE_CONFIG_CURRENT_SCHEMA_VERSION,
  MOBILE_CONFIG_FEATURE_KEYS,
  MOBILE_CONFIG_NAVIGATION_ICONS,
  MOBILE_CONFIG_NAVIGATION_LABEL_MAX_LENGTH,
  MOBILE_CONFIG_NAVIGATION_MAX_ITEMS,
  MOBILE_CONFIG_NAVIGATION_MIN_ITEMS,
  MOBILE_CONFIG_NAVIGATION_TYPES,
  MOBILE_CONFIG_PAGE_NAMES,
  type MobileConfigDocument,
  type MobileConfigFeatures,
  type MobileConfigNavigationIcon,
  type MobileConfigPage,
  type MobileConfigPageName,
} from "../types";

// ── Style constants (mirrors ThemeEditorSection.tsx / AppBuilderCanvas.tsx's conventions — this
// feature area has no shadcn form primitives of its own) ──────────────────────────────────────

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

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "7px 10px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  cursor: "pointer",
  appearance: "none",
  backgroundImage:
    "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%236B7280' d='M6 8L1 3h10z'/%3E%3C/svg%3E\")",
  backgroundRepeat: "no-repeat",
  backgroundPosition: "right 10px center",
  paddingRight: 28,
};

const hintStyle: React.CSSProperties = { color: "#4B5563", fontSize: 11, marginTop: 4 };
const errorStyle: React.CSSProperties = { color: "#F87171", fontSize: 11, marginTop: 4 };

// ── Icon lookup (TASK-542) ──────────────────────────────────────────────────────────────────
// MobileConfigWhitelists.NavigationIcons are semantic keys the mobile app's own icon registry
// resolves natively — no icon-key→lucide-component mapping existed anywhere in this codebase
// before this task (checked `mobile/features/mobile-config/*` and `frontend/features/consumer-app`
// — the mobile side maps these keys to its own native icon components, not lucide). This mapping
// is admin-preview-only, kept local to this file the same way AppBuilderCanvas.tsx keeps its own
// `BLOCK_ICONS` map local rather than a shared file, since nothing else in the frontend needs it.

const NAVIGATION_ICON_COMPONENTS: Record<MobileConfigNavigationIcon, LucideIcon> = {
  home: Home,
  tag: Tag,
  grid: Grid3x3,
  qr: QrCode,
  ticket: Ticket,
  map: MapIcon,
  news: Newspaper,
  user: User,
};

// ── Seed document (TASK-539's buildSeedDocument precedent) ─────────────────────────────────────
// This screen edits the same whole-document draft AppBuilderCanvas.tsx does (TASK-538b's PUT takes
// the *whole* config, not just `navigation`), so a brand-new tenant visiting Navigation Builder
// before ever opening App Builder still needs a complete, valid seed to read-modify-write against.
// Deliberately duplicated (not imported) rather than refactoring AppBuilderCanvas.tsx to export it
// — TASK-542's scope is this screen only. Kept byte-identical to AppBuilderCanvas.tsx's own
// `buildSeedDocument` (same feature-flag defaults, same four scaffolded pages, same starting
// navigation) so whichever Retailer Admin screen a tenant opens first produces the same seed shape;
// if AppBuilderCanvas.tsx's seed ever changes, update this copy too.

function buildSeedDocument(): MobileConfigDocument {
  const features = Object.fromEntries(
    MOBILE_CONFIG_FEATURE_KEYS.map((key) => [key, false]),
  ) as MobileConfigFeatures;

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

// ── Validation (mirrors MobileConfigWhitelists.cs / MobileConfigValidator.cs exactly for count,
// type, and icon; `label` additionally caps length client-side only — see
// MOBILE_CONFIG_NAVIGATION_LABEL_MAX_LENGTH's remarks in ../types.ts) ──────────────────────────

function buildSchema(t: ReturnType<typeof useTranslations>) {
  const item = z.object({
    type: z.enum(MOBILE_CONFIG_NAVIGATION_TYPES, { errorMap: () => ({ message: t("typeInvalidError") }) }),
    label: z
      .string()
      .trim()
      .min(1, t("labelRequiredError"))
      .max(MOBILE_CONFIG_NAVIGATION_LABEL_MAX_LENGTH, t("labelMaxLengthError", { max: MOBILE_CONFIG_NAVIGATION_LABEL_MAX_LENGTH })),
    icon: z.enum(MOBILE_CONFIG_NAVIGATION_ICONS, { errorMap: () => ({ message: t("iconInvalidError") }) }),
  });

  return z.object({
    navigation: z
      .array(item)
      .min(MOBILE_CONFIG_NAVIGATION_MIN_ITEMS, t("countError", { min: MOBILE_CONFIG_NAVIGATION_MIN_ITEMS, max: MOBILE_CONFIG_NAVIGATION_MAX_ITEMS }))
      .max(MOBILE_CONFIG_NAVIGATION_MAX_ITEMS, t("countError", { min: MOBILE_CONFIG_NAVIGATION_MIN_ITEMS, max: MOBILE_CONFIG_NAVIGATION_MAX_ITEMS })),
  });
}

type FormValues = z.infer<ReturnType<typeof buildSchema>>;
type NavFormItem = FormValues["navigation"][number];

// Backend field paths look like `navigation[2].label` (MobileConfigValidator's `field` format);
// react-hook-form's setError needs dot+index paths (`navigation.2.label`). Only navigation-item
// field errors map onto a specific row/field — anything else (the `navigation` count error, or an
// unrelated top-level field from a document this screen didn't create) falls back to the generic
// banner, same convention ThemeEditorSection.tsx's `mappedAny` fallback uses.
const NAV_ITEM_FIELD_PATTERN = /^navigation\[(\d+)]\.(type|label|icon)$/;

function newItem(): NavFormItem {
  return { type: "promotions", label: "", icon: "tag" };
}

/**
 * TASK-542: Navigation Builder — the last piece of Stage C. Fills the `/consumer-app/navigation`
 * placeholder TASK-535 scaffolded, editing the same whole-document draft AppBuilderCanvas.tsx
 * (TASK-539/541) and ThemeEditorSection.tsx (TASK-537) already read/write, via the same
 * `useMobileConfigDraft`/`useSaveMobileConfigDraft` hooks (TASK-538b). This screen only ever
 * touches `document.navigation` — `features`/`pages`/`schemaVersion` are carried through untouched
 * on every save (kept in `configDoc` state, merged back in on submit), exactly like
 * `AppBuilderCanvas.tsx`'s `withPageBlocks` read-modify-write, just for a single array field
 * instead of a keyed page map.
 *
 * Reordering uses `@dnd-kit/sortable` (already a dependency, already used by
 * `AppBuilderCanvas.tsx` for block reordering) rather than up/down buttons — the list is short
 * (2–5 items) but drag handles are the established pattern in this feature area, and reusing it
 * keeps the interaction model consistent across every Retailer Admin builder screen.
 */
export function NavigationBuilderSection() {
  const t = useTranslations("Dashboard.consumerApp.navigationBuilder");
  const draftQuery = useMobileConfigDraft();
  const save = useSaveMobileConfigDraft();

  // Holds the full document minus `navigation` (which react-hook-form owns below) — same
  // read-modify-write shape as AppBuilderCanvas.tsx's `configDoc`, just narrower in scope.
  const [restOfDoc, setRestOfDoc] = useState<Omit<MobileConfigDocument, "navigation"> | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const schema = buildSchema(t);

  const {
    control,
    handleSubmit,
    register,
    reset,
    setError,
    formState: { errors, isDirty },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { navigation: [] },
  });

  const { fields, append, remove, move } = useFieldArray({ control, name: "navigation" });

  // TASK-546: warns before losing unsaved edits (tab-close/refresh always; in-app link-click
  // navigation best-effort) — see useUnsavedChangesGuard.ts's remarks for exact coverage.
  useUnsavedChangesGuard(isDirty, t("unsavedChangesWarning"));

  // Hydrate from the server — brand-new tenant gets the seed document instead of a fabricated
  // draft (see buildSeedDocument's remarks above). Re-runs whenever draftQuery.data's reference
  // changes, including right after a successful save (same convention ThemeEditorSection's/
  // AppBuilderCanvas.tsx's re-sync effects use).
  useEffect(() => {
    if (!draftQuery.data) return;
    const doc: MobileConfigDocument =
      draftQuery.data.hasDraft && draftQuery.data.configurationJson
        ? (JSON.parse(draftQuery.data.configurationJson) as MobileConfigDocument)
        : buildSeedDocument();
    const { navigation, ...rest } = doc;
    setRestOfDoc(rest);
    // Cast: `MobileConfigNavigationItem.icon` is a loose `string` (mirrors the JSON shape as
    // parsed), while the form's zod-inferred type narrows it to the whitelist enum. A value
    // outside the current whitelist (e.g. from a draft saved before a whitelist change) still
    // round-trips as a string here; the form's own validation catches it on submit.
    reset({ navigation: (navigation ?? []) as FormValues["navigation"] });
  }, [draftQuery.data, reset]);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 4 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) return;
    const oldIndex = fields.findIndex((f) => f.id === active.id);
    const newIndex = fields.findIndex((f) => f.id === over.id);
    if (oldIndex === -1 || newIndex === -1) return;
    move(oldIndex, newIndex);
  }

  function handleAdd() {
    if (fields.length >= MOBILE_CONFIG_NAVIGATION_MAX_ITEMS) return;
    append(newItem());
  }

  function handleRemove(index: number) {
    if (fields.length <= MOBILE_CONFIG_NAVIGATION_MIN_ITEMS) return;
    remove(index);
  }

  async function onValid(values: FormValues) {
    if (!restOfDoc || save.isPending) return;
    setSaveError(null);
    const nextDoc: MobileConfigDocument = { ...restOfDoc, navigation: values.navigation };
    try {
      await save.mutateAsync({ configurationJson: JSON.stringify(nextDoc) });
      toast.success(t("saveSuccess"));
    } catch (err) {
      const fieldErrors = extractDraftValidationErrors(err);
      if (!fieldErrors) {
        setSaveError(err instanceof Error ? err.message : t("saveError"));
        return;
      }
      const unmapped: string[] = [];
      for (const fe of fieldErrors) {
        const match = NAV_ITEM_FIELD_PATTERN.exec(fe.field);
        if (match) {
          const path = `navigation.${match[1]}.${match[2]}` as Path<FormValues>;
          setError(path, { type: "server", message: fe.message });
        } else {
          unmapped.push(`${fe.field}: ${fe.message}`);
        }
      }
      if (unmapped.length > 0) setSaveError(unmapped.join(" "));
    }
  }

  if (draftQuery.isLoading || !restOfDoc) {
    return <div style={{ ...cardStyle, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>;
  }
  if (draftQuery.isError) {
    return <div style={{ ...cardStyle, color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>;
  }

  const atMin = fields.length <= MOBILE_CONFIG_NAVIGATION_MIN_ITEMS;
  const atMax = fields.length >= MOBILE_CONFIG_NAVIGATION_MAX_ITEMS;

  return (
    <div style={{ ...cardStyle, maxWidth: 760 }}>
      <p style={sectionLabelStyle}>{t("sectionLabel")}</p>
      <p style={{ color: "#4B5563", fontSize: 12, margin: "0 0 16px" }}>
        {t("hint", { min: MOBILE_CONFIG_NAVIGATION_MIN_ITEMS, max: MOBILE_CONFIG_NAVIGATION_MAX_ITEMS })}
      </p>

      {/* Array-level (count) error — react-hook-form surfaces `useFieldArray` min/max issues under
          `.root`, distinct from per-item errors indexed on the same object (RHF convention). */}
      {errors.navigation?.root?.message && <p style={errorStyle}>{errors.navigation.root.message}</p>}

      <form onSubmit={handleSubmit(onValid)}>
        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
          <SortableContext items={fields.map((f) => f.id)} strategy={verticalListSortingStrategy}>
            <div style={{ display: "flex", flexDirection: "column", gap: 8, marginTop: 8 }}>
              {fields.map((field, index) => (
                <NavItemRow
                  key={field.id}
                  id={field.id}
                  index={index}
                  control={control}
                  register={register}
                  errors={errors}
                  onRemove={() => handleRemove(index)}
                  removeDisabled={atMin}
                  removeDisabledHint={t("removeBlockedHint", { min: MOBILE_CONFIG_NAVIGATION_MIN_ITEMS })}
                  t={t}
                />
              ))}
            </div>
          </SortableContext>
        </DndContext>

        <div style={{ marginTop: 12 }}>
          <Btn variant="ghost" size="sm" onClick={handleAdd} disabled={atMax} icon={<Plus size={13} />}>
            {t("addButton")}
          </Btn>
          {atMax && <span style={{ ...hintStyle, marginLeft: 10 }}>{t("addBlockedHint", { max: MOBILE_CONFIG_NAVIGATION_MAX_ITEMS })}</span>}
        </div>

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

        {saveError && <p style={{ ...errorStyle, marginTop: 12 }}>{saveError}</p>}

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
          <Btn type="submit" disabled={save.isPending || !isDirty}>
            {save.isPending ? t("savingButton") : t("saveButton")}
          </Btn>
          {!isDirty && <span style={{ color: "#4B5563", fontSize: 12 }}>{t("noChanges")}</span>}
        </div>
      </form>
    </div>
  );
}

// ── One draggable navigation-item row ───────────────────────────────────────────────────────

interface NavItemRowProps {
  id: string;
  index: number;
  control: Control<FormValues>;
  register: ReturnType<typeof useForm<FormValues>>["register"];
  errors: ReturnType<typeof useForm<FormValues>>["formState"]["errors"];
  onRemove: () => void;
  removeDisabled: boolean;
  removeDisabledHint: string;
  t: ReturnType<typeof useTranslations>;
}

function NavItemRow({
  id,
  index,
  control,
  register,
  errors,
  onRemove,
  removeDisabled,
  removeDisabledHint,
  t,
}: NavItemRowProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id });
  const rowErrors = errors.navigation?.[index];

  return (
    <div
      ref={setNodeRef}
      style={{
        display: "flex",
        alignItems: "flex-start",
        gap: 8,
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
        aria-label={t("dragHandleLabel")}
        style={{
          display: "flex",
          alignItems: "center",
          height: 34,
          cursor: isDragging ? "grabbing" : "grab",
          color: "#4B5563",
          touchAction: "none",
          flexShrink: 0,
        }}
      >
        <GripVertical size={16} />
      </span>

      <div style={{ display: "grid", gridTemplateColumns: "1fr 1.4fr 1fr", gap: 10, flex: 1, minWidth: 0 }}>
        <div>
          <select {...register(`navigation.${index}.type` as const)} style={selectStyle}>
            {MOBILE_CONFIG_NAVIGATION_TYPES.map((type) => (
              <option key={type} value={type}>
                {t(`navTypes.${type}`)}
              </option>
            ))}
          </select>
          {rowErrors?.type && <p style={errorStyle}>{rowErrors.type.message}</p>}
        </div>

        <div>
          <input
            {...register(`navigation.${index}.label` as const)}
            placeholder={t("labelPlaceholder")}
            maxLength={MOBILE_CONFIG_NAVIGATION_LABEL_MAX_LENGTH}
            style={{ ...inputStyle, borderColor: rowErrors?.label ? "#EF4444" : "#374151" }}
          />
          {rowErrors?.label && <p style={errorStyle}>{rowErrors.label.message}</p>}
        </div>

        <IconField
          index={index}
          control={control}
          register={register}
          error={rowErrors?.icon?.message}
          t={t}
        />
      </div>

      <button
        type="button"
        onClick={onRemove}
        disabled={removeDisabled}
        aria-label={t("removeButton")}
        title={removeDisabled ? removeDisabledHint : t("removeButton")}
        style={{
          width: 30,
          height: 30,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "transparent",
          border: "1px solid #374151",
          borderRadius: 6,
          color: removeDisabled ? "#374151" : "#9CA3AF",
          cursor: removeDisabled ? "not-allowed" : "pointer",
          flexShrink: 0,
        }}
      >
        <Trash2 size={14} />
      </button>
    </div>
  );
}

// Reads the row's own current icon value via `useWatch` (for the live icon-preview swatch next to
// the select) — correctly reflects both the value hydrated from the loaded/seed document and any
// in-progress edit, without a separate local-state/onChange dance.
function IconField({
  index,
  control,
  register,
  error,
  t,
}: {
  index: number;
  control: Control<FormValues>;
  register: ReturnType<typeof useForm<FormValues>>["register"];
  error?: string;
  t: ReturnType<typeof useTranslations>;
}) {
  const current = useWatch({ control, name: `navigation.${index}.icon` as const });
  const Icon = NAVIGATION_ICON_COMPONENTS[current as MobileConfigNavigationIcon] ?? Home;

  return (
    <div>
      <div style={{ display: "flex", alignItems: "center", gap: 6 }}>
        <span
          style={{
            width: 30,
            height: 30,
            flexShrink: 0,
            borderRadius: 6,
            background: "#0B0E14",
            border: "1px solid #374151",
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
          }}
        >
          <Icon size={14} style={{ color: "#9CA3AF" }} />
        </span>
        <select {...register(`navigation.${index}.icon` as const)} style={{ ...selectStyle, flex: 1 }}>
          {MOBILE_CONFIG_NAVIGATION_ICONS.map((icon) => (
            <option key={icon} value={icon}>
              {t(`navIcons.${icon}`)}
            </option>
          ))}
        </select>
      </div>
      {error && <p style={errorStyle}>{error}</p>}
    </div>
  );
}
