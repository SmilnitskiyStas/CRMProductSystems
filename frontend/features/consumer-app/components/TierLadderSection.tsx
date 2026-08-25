"use client";

import { useEffect, useState } from "react";
import { useForm, useFieldArray, type Control } from "react-hook-form";
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
import { GripVertical, Plus, Trash2 } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { ApiError } from "@/lib/api";
import { useLoyaltyTiers, useUpdateLoyaltyTiers } from "../hooks/useLoyaltyTiers";
import { useUnsavedChangesGuard } from "../hooks/useUnsavedChangesGuard";
import { ConfirmDialog } from "./ConfirmDialog";
import type { LoyaltyTierDefinitionDto, UpsertTierRequest } from "../types";

// ── Style constants (mirrors NavigationBuilderSection.tsx / BonusProgramSection.tsx —
// this feature area has no shadcn form primitives of its own) ──────────────────────────────

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

const hintStyle: React.CSSProperties = { color: "#4B5563", fontSize: 11, marginTop: 4 };
const errorStyle: React.CSSProperties = { color: "#F87171", fontSize: 11, marginTop: 4 };
const columnLabelStyle: React.CSSProperties = {
  color: "#6B7280",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: 0.3,
};

// ── Validation (mirrors LoyaltyService.UpsertTierLadderAsync's server-side checks exactly —
// name required/<=100 chars, MinCompositeScore >= 0, AccrualMultiplier in [0, 999.99],
// DiscountPercent in [0, 100] — so the client never round-trips an obviously-invalid value.
// SortOrder uniqueness isn't a client-facing field at all: this component never exposes a raw
// SortOrder input, it always derives it from a row's 0-based position in the on-screen list on
// submit (see buildRequestBody below), so duplicates are structurally impossible from this UI. ──

function buildSchema(t: ReturnType<typeof useTranslations>) {
  const row = z.object({
    name: z
      .string()
      .trim()
      .min(1, t("nameRequiredError"))
      .max(100, t("nameMaxLengthError", { max: 100 })),
    minCompositeScore: z
      .number({ invalid_type_error: t("numberRequiredError") })
      .min(0, t("nonNegativeError")),
    accrualMultiplier: z
      .number({ invalid_type_error: t("numberRequiredError") })
      .min(0, t("nonNegativeError"))
      .max(999.99, t("multiplierRangeError")),
    discountPercent: z
      .number({ invalid_type_error: t("numberRequiredError") })
      .min(0, t("nonNegativeError"))
      .max(100, t("percentRangeError")),
    /**
     * Internal-only, never sent to the backend (stripped in buildRequestBody). Null for a row
     * added in this editing session; otherwise the exact `sortOrder` this row held the last time
     * it was loaded from (or saved to) the server. Compared against the row's on-submit array
     * index to detect a reorder that would reassign an existing tier's identity — see
     * `hasIdentityShiftingReorder` below.
     */
    originalSortOrder: z.number().nullable(),
  });

  return z.object({ tiers: z.array(row) });
}

type FormValues = z.infer<ReturnType<typeof buildSchema>>;
type TierFormRow = FormValues["tiers"][number];

function newRow(): TierFormRow {
  return { name: "", minCompositeScore: 0, accrualMultiplier: 1, discountPercent: 0, originalSortOrder: null };
}

function toFormRow(tier: LoyaltyTierDefinitionDto, index: number): TierFormRow {
  return {
    name: tier.name,
    minCompositeScore: tier.minCompositeScore,
    accrualMultiplier: tier.accrualMultiplier,
    discountPercent: tier.discountPercent,
    // Not tier.sortOrder verbatim: after a save the backend always returns rows renumbered
    // 0..n-1 in this exact order (this component's own convention, see buildRequestBody), so
    // `index` and `tier.sortOrder` are equal in that case. Using `index` here also degrades
    // gracefully for a ladder never touched by this UI (e.g. seeded directly via Swagger with
    // non-contiguous SortOrder values per the plan's manual-test note) — the first save through
    // this screen will then correctly flag every row as "reordered" (its true SortOrder is about
    // to be renumbered) and warn the admin, rather than silently renumbering without asking.
    originalSortOrder: index,
  };
}

/**
 * Renumbers every row's `sortOrder` to its 0-based position in the final on-screen list. Always
 * full-renumber (not "only touched rows") so the ladder's visual order is always exactly what
 * reloads after save — GET orders by `sortOrder` ascending, so the numeric value has to match
 * final position for WYSIWYG to hold, not just the array order submitted.
 */
function buildRequestBody(values: FormValues): UpsertTierRequest[] {
  return values.tiers.map((row, index) => ({
    name: row.name.trim(),
    sortOrder: index,
    minCompositeScore: row.minCompositeScore,
    accrualMultiplier: row.accrualMultiplier,
    discountPercent: row.discountPercent,
  }));
}

/**
 * True when saving would change an already-saved row's `sortOrder` — i.e. its final index no
 * longer equals the `sortOrder` it held server-side. This is the exact case the TASK-615 handoff
 * flags: LoyaltyService.UpsertTierLadderAsync matches submitted rows against existing ones purely
 * by `sortOrder` value, so this doesn't just affect the row(s) actually dragged — adding or
 * removing a row above an existing one shifts every following row's effective `sortOrder` too,
 * which can silently swap which database record (and therefore which Id any
 * LoyaltyMembership.CurrentTierId already points at) ends up holding a given row's edited values.
 * A brand-new row (`originalSortOrder === null`) never trips this — it has no prior identity to
 * lose.
 */
function hasIdentityShiftingReorder(values: FormValues): boolean {
  return values.tiers.some((row, index) => row.originalSortOrder !== null && row.originalSortOrder !== index);
}

/**
 * TASK-620: "Драбина рангів" — per-tenant loyalty tier ladder editor on the Consumer App admin
 * area, backed by TASK-615's `api/settings/loyalty/tiers` bulk-replace-by-SortOrder endpoint.
 * Follows NavigationBuilderSection.tsx's react-hook-form + useFieldArray + @dnd-kit/sortable
 * pattern (add/remove/drag-reorder over a short list) rather than BonusProgramSection.tsx's plain
 * useState form, since this screen needs the same reorderable-list interaction. Unlike that draft/
 * publish screen, this one has no draft layer — Save calls the settings endpoint directly, same
 * immediate-effect convention as BonusProgramSection.
 */
export function TierLadderSection() {
  const t = useTranslations("Dashboard.consumerApp.tierLadder");
  const query = useLoyaltyTiers();
  const update = useUpdateLoyaltyTiers();

  const [saveError, setSaveError] = useState<string | null>(null);
  const [pendingValues, setPendingValues] = useState<FormValues | null>(null);

  const schema = buildSchema(t);

  const {
    control,
    handleSubmit,
    register,
    reset,
    formState: { errors, isDirty },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { tiers: [] },
  });

  const { fields, append, remove, move } = useFieldArray({ control, name: "tiers" });

  useUnsavedChangesGuard(isDirty, t("unsavedChangesWarning"));

  // Hydrate from the server. Re-runs after a successful save too (query.data's reference
  // changes on invalidate), which re-syncs `originalSortOrder` against the freshly-saved,
  // now-contiguous values — see toFormRow's remarks.
  useEffect(() => {
    if (!query.data) return;
    reset({ tiers: query.data.map(toFormRow) });
  }, [query.data, reset]);

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

  /** Returns whether the save succeeded — callers use this to decide whether to close the
   *  reorder-confirmation dialog (kept open on failure so the error shows where the admin is
   *  looking, same convention ConfirmDialog's own `error` prop / VersionHistorySection use). */
  async function doSave(values: FormValues): Promise<boolean> {
    if (update.isPending) return false;
    setSaveError(null);
    try {
      const result = await update.mutateAsync(buildRequestBody(values));
      reset({ tiers: result.map(toFormRow) });
      toast.success(t("saveSuccess"));
      return true;
    } catch (err) {
      setSaveError(err instanceof ApiError || err instanceof Error ? err.message : t("saveError"));
      return false;
    }
  }

  function onValid(values: FormValues) {
    if (hasIdentityShiftingReorder(values)) {
      setPendingValues(values);
      return;
    }
    void doSave(values);
  }

  if (query.isLoading) {
    return <div style={{ ...cardStyle, color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>;
  }
  if (query.isError) {
    return <div style={{ ...cardStyle, color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>;
  }

  return (
    <div style={{ ...cardStyle, maxWidth: 820 }}>
      <p style={sectionLabelStyle}>{t("sectionLabel")}</p>
      <p style={{ color: "#4B5563", fontSize: 12, margin: "0 0 16px" }}>{t("hint")}</p>

      <form onSubmit={handleSubmit(onValid)}>
        {fields.length > 0 && (
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "20px 1.6fr 1fr 1fr 1fr 30px",
              gap: 10,
              padding: "0 12px",
              marginBottom: 6,
            }}
          >
            <span />
            <span style={columnLabelStyle}>{t("nameLabel")}</span>
            <span style={columnLabelStyle}>{t("minScoreLabel")}</span>
            <span style={columnLabelStyle}>{t("multiplierLabel")}</span>
            <span style={columnLabelStyle}>{t("discountLabel")}</span>
            <span />
          </div>
        )}

        <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
          <SortableContext items={fields.map((f) => f.id)} strategy={verticalListSortingStrategy}>
            <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
              {fields.map((field, index) => (
                <TierRow
                  key={field.id}
                  id={field.id}
                  index={index}
                  control={control}
                  register={register}
                  errors={errors}
                  onRemove={() => remove(index)}
                  removeLabel={t("removeButton")}
                  namePlaceholder={t("namePlaceholder")}
                />
              ))}
            </div>
          </SortableContext>
        </DndContext>

        {fields.length === 0 && <p style={{ ...hintStyle, marginTop: 0 }}>{t("emptyHint")}</p>}

        <div style={{ marginTop: 12 }}>
          <Btn variant="ghost" size="sm" onClick={() => append(newRow())} icon={<Plus size={13} />}>
            {t("addButton")}
          </Btn>
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
          <Btn type="submit" disabled={update.isPending || !isDirty}>
            {update.isPending ? t("savingButton") : t("saveButton")}
          </Btn>
          {!isDirty && <span style={{ color: "#4B5563", fontSize: 12 }}>{t("noChanges")}</span>}
        </div>
      </form>

      {pendingValues && (
        <ConfirmDialog
          title={t("reorderConfirmTitle")}
          description={t("reorderConfirmDescription")}
          confirmLabel={t("reorderConfirmButton")}
          cancelLabel={t("reorderCancelButton")}
          variant="primary"
          pending={update.isPending}
          error={saveError}
          onConfirm={() => {
            const values = pendingValues;
            void doSave(values).then((ok) => {
              if (ok) setPendingValues(null);
            });
          }}
          onClose={() => setPendingValues(null)}
        />
      )}
    </div>
  );
}

// ── One draggable tier row ──────────────────────────────────────────────────────────────────

interface TierRowProps {
  id: string;
  index: number;
  control: Control<FormValues>;
  register: ReturnType<typeof useForm<FormValues>>["register"];
  errors: ReturnType<typeof useForm<FormValues>>["formState"]["errors"];
  onRemove: () => void;
  removeLabel: string;
  namePlaceholder: string;
}

function TierRow({ id, index, register, errors, onRemove, removeLabel, namePlaceholder }: TierRowProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id });
  const rowErrors = errors.tiers?.[index];

  return (
    <div
      ref={setNodeRef}
      style={{
        display: "grid",
        gridTemplateColumns: "20px 1.6fr 1fr 1fr 1fr 30px",
        gap: 10,
        alignItems: "flex-start",
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
          height: 32,
          cursor: isDragging ? "grabbing" : "grab",
          color: "#4B5563",
          touchAction: "none",
        }}
      >
        <GripVertical size={16} />
      </span>

      <div>
        <input
          {...register(`tiers.${index}.name` as const)}
          placeholder={namePlaceholder}
          style={{ ...inputStyle, borderColor: rowErrors?.name ? "#EF4444" : "#374151" }}
        />
        {rowErrors?.name && <p style={errorStyle}>{rowErrors.name.message}</p>}
      </div>

      <div>
        <input
          type="number"
          step="0.01"
          min={0}
          {...register(`tiers.${index}.minCompositeScore` as const, { valueAsNumber: true })}
          style={{ ...inputStyle, borderColor: rowErrors?.minCompositeScore ? "#EF4444" : "#374151" }}
        />
        {rowErrors?.minCompositeScore && <p style={errorStyle}>{rowErrors.minCompositeScore.message}</p>}
      </div>

      <div>
        <input
          type="number"
          step="0.01"
          min={0}
          max={999.99}
          {...register(`tiers.${index}.accrualMultiplier` as const, { valueAsNumber: true })}
          style={{ ...inputStyle, borderColor: rowErrors?.accrualMultiplier ? "#EF4444" : "#374151" }}
        />
        {rowErrors?.accrualMultiplier && <p style={errorStyle}>{rowErrors.accrualMultiplier.message}</p>}
      </div>

      <div>
        <input
          type="number"
          step="0.01"
          min={0}
          max={100}
          {...register(`tiers.${index}.discountPercent` as const, { valueAsNumber: true })}
          style={{ ...inputStyle, borderColor: rowErrors?.discountPercent ? "#EF4444" : "#374151" }}
        />
        {rowErrors?.discountPercent && <p style={errorStyle}>{rowErrors.discountPercent.message}</p>}
      </div>

      <button
        type="button"
        onClick={onRemove}
        aria-label={removeLabel}
        title={removeLabel}
        style={{
          width: 30,
          height: 30,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          background: "transparent",
          border: "1px solid #374151",
          borderRadius: 6,
          color: "#9CA3AF",
          cursor: "pointer",
        }}
      >
        <Trash2 size={14} />
      </button>
    </div>
  );
}
