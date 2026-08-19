"use client";

import { useEffect, useMemo, useState } from "react";
import { useForm, type UseFormRegister, type UseFormSetValue } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z, type ZodTypeAny } from "zod";
import { useTranslations } from "next-intl";
import { Info, X } from "lucide-react";
import { DetailDrawer } from "@/components/ui/DetailDrawer";
import { Btn } from "@/components/ui/Btn";
import type { BlockDefinitionDto, BlockPropDefinitionDto, MobileConfigBlockInstance } from "../types";

// ── Style constants (mirrors ThemeEditorSection.tsx / AppBuilderCanvas.tsx conventions — this
// feature area has no shadcn form primitives of its own) ───────────────────────────────────────

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
const errorStyle: React.CSSProperties = { ...hintStyle, color: "#EF4444" };
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

function isValidHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}

function capitalize(value: string): string {
  return value.length === 0 ? value : value.charAt(0).toUpperCase() + value.slice(1);
}

/**
 * `BlockPropDefinition` (backend) carries no per-field display label — only `name`/`type`/bounds
 * (see `BlockPropDefinition.cs`'s own remarks on why it's a flat descriptor, not a full schema
 * doc). Deriving the label mechanically from the field's camelCase name is what lets this editor
 * render ANY future registry prop — including ones added to a brand-new block type — with zero
 * code changes here, at the cost of a slightly generic label ("Image Url" rather than a
 * hand-tuned "Image URL"). Logged as a deliberate tradeoff in the task log rather than a gap.
 */
function humanizeFieldName(name: string): string {
  const withSpaces = name
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2");
  return capitalize(withSpaces);
}

type PropT = ReturnType<typeof useTranslations>;
type PropFormValues = Record<string, unknown>;

// ── Dynamic zod schema (TASK-540 core requirement): one schema built field-by-field from each
// block's own `BlockPropDefinition[]`, switching only on `Type` — never one static schema per
// block type. ─────────────────────────────────────────────────────────────────────────────────

function stringFieldSchema(def: BlockPropDefinitionDto, t: PropT): ZodTypeAny {
  let schema = z.string();
  if (typeof def.maxLength === "number") {
    schema = schema.max(def.maxLength, t("maxLengthError", { max: def.maxLength }));
  }
  const minLen = def.required ? Math.max(def.minLength ?? 1, 1) : def.minLength ?? 0;
  if (minLen > 0) {
    const message =
      def.required && (def.minLength ?? 0) <= 1
        ? t("requiredError")
        : t("minLengthError", { min: minLen });
    schema = schema.min(minLen, message);
  }
  return schema;
}

function intFieldSchema(def: BlockPropDefinitionDto, t: PropT): ZodTypeAny {
  let schema = z.number({ invalid_type_error: t("numberRequiredError") }).int(t("integerError"));
  if (typeof def.min === "number") schema = schema.min(def.min, t("rangeMinError", { min: def.min }));
  if (typeof def.max === "number") schema = schema.max(def.max, t("rangeMaxError", { max: def.max }));
  return schema;
}

function enumFieldSchema(def: BlockPropDefinitionDto, t: PropT): ZodTypeAny {
  const allowed = def.allowedValues ?? [];
  return z.string().refine((v) => allowed.includes(v), { message: t("invalidOptionError") });
}

function urlFieldSchema(def: BlockPropDefinitionDto, t: PropT): ZodTypeAny {
  let schema = z.string();
  if (typeof def.maxLength === "number") {
    schema = schema.max(def.maxLength, t("maxLengthError", { max: def.maxLength }));
  }
  return schema.refine(
    (v) => {
      const trimmed = v.trim();
      return trimmed === "" ? !def.required : isValidHttpUrl(trimmed);
    },
    { message: def.required ? t("urlRequiredError") : t("urlFormatError") },
  );
}

function stringArrayFieldSchema(def: BlockPropDefinitionDto, t: PropT): ZodTypeAny {
  let schema = z.array(z.string());
  const minItems = def.required ? Math.max(def.minItems ?? 1, 1) : def.minItems ?? 0;
  if (minItems > 0) schema = schema.min(minItems, t("minItemsError", { min: minItems }));
  if (typeof def.maxItems === "number") {
    schema = schema.max(def.maxItems, t("maxItemsError", { max: def.maxItems }));
  }
  if (def.allowedValues) {
    const allowed = def.allowedValues;
    // `.refine()` returns a `ZodEffects` wrapper, not a `ZodArray` — return it directly instead
    // of reassigning back into `schema` (which TS has narrowed to `ZodArray<ZodString>`).
    return schema.refine((arr) => arr.every((v) => allowed.includes(v)), {
      message: t("invalidOptionError"),
    });
  }
  return schema;
}

/** The one place this editor switches on `BlockPropDefinition.Type` — six cases, matching
 *  `BlockPropTypes.cs` exactly. A future 7th type falls through to a permissive schema instead
 *  of crashing the form. */
function fieldSchemaFor(def: BlockPropDefinitionDto, t: PropT): ZodTypeAny {
  switch (def.type) {
    case "string":
      return stringFieldSchema(def, t);
    case "int":
      return intFieldSchema(def, t);
    case "bool":
      return z.boolean();
    case "enum":
      return enumFieldSchema(def, t);
    case "url":
      return urlFieldSchema(def, t);
    case "stringArray":
      return stringArrayFieldSchema(def, t);
    default:
      return z.unknown();
  }
}

function buildPropsSchema(schema: BlockPropDefinitionDto[], t: PropT) {
  const shape: Record<string, ZodTypeAny> = {};
  for (const def of schema) shape[def.name] = fieldSchemaFor(def, t);
  return z.object(shape);
}

function coerceValue(def: BlockPropDefinitionDto, value: unknown): unknown {
  switch (def.type) {
    case "bool":
      return typeof value === "boolean" ? value : Boolean(def.default);
    case "int":
      return typeof value === "number" && Number.isFinite(value) ? value : Number(def.default ?? 0) || 0;
    case "stringArray":
      if (Array.isArray(value)) return value.filter((v): v is string => typeof v === "string");
      return Array.isArray(def.default) ? def.default : [];
    case "string":
    case "url":
    case "enum":
      return typeof value === "string" ? value : typeof def.default === "string" ? def.default : "";
    default:
      return value ?? def.default;
  }
}

function buildDefaultValues(
  schema: BlockPropDefinitionDto[],
  currentProps: Record<string, unknown>,
): PropFormValues {
  const values: PropFormValues = {};
  for (const def of schema) {
    const raw = def.name in currentProps ? currentProps[def.name] : def.default;
    values[def.name] = coerceValue(def, raw);
  }
  return values;
}

// ── Field renderer (schema-driven — switches ONLY on `def.type`, never on the block's own
// `type`; adding a new block type to the Registry never requires touching this file) ───────────

interface FieldProps {
  def: BlockPropDefinitionDto;
  label: string;
  register: UseFormRegister<PropFormValues>;
  setValue: UseFormSetValue<PropFormValues>;
  value: unknown;
  error?: string;
  t: PropT;
}

function RequiredMark({ required }: { required: boolean }) {
  return required ? <span style={{ color: "#F87171" }}> *</span> : null;
}

function TextField({ def, label, register, error, t, isUrl }: FieldProps & { isUrl?: boolean }) {
  return (
    <div>
      <label style={labelStyle}>
        {label}
        <RequiredMark required={def.required} />
      </label>
      <input
        {...register(def.name)}
        placeholder={isUrl ? "https://example.com" : undefined}
        style={{ ...inputStyle, borderColor: error ? "#EF4444" : "#374151" }}
      />
      {error && <p style={errorStyle}>{error}</p>}
      {!error && def.maxLength != null && (
        <p style={hintStyle}>{t("maxLengthHint", { max: def.maxLength })}</p>
      )}
    </div>
  );
}

function NumberField({ def, label, register, error, t }: FieldProps) {
  return (
    <div>
      <label style={labelStyle}>
        {label}
        <RequiredMark required={def.required} />
      </label>
      <input
        type="number"
        min={def.min ?? undefined}
        max={def.max ?? undefined}
        step={1}
        {...register(def.name, { valueAsNumber: true })}
        style={{ ...inputStyle, borderColor: error ? "#EF4444" : "#374151" }}
      />
      {error && <p style={errorStyle}>{error}</p>}
      {!error && (def.min != null || def.max != null) && (
        <p style={hintStyle}>
          {t("rangeHint", { min: def.min ?? "−∞", max: def.max ?? "∞" })}
        </p>
      )}
    </div>
  );
}

function BoolField({ def, label, register }: FieldProps) {
  return (
    <label style={{ display: "flex", alignItems: "center", gap: 10, cursor: "pointer" }}>
      <input
        type="checkbox"
        {...register(def.name)}
        style={{ width: 16, height: 16, accentColor: "#3B82F6", cursor: "pointer" }}
      />
      <span style={{ color: "#E8EDF5", fontSize: 13 }}>{label}</span>
    </label>
  );
}

function EnumField({ def, label, register, error }: FieldProps) {
  const options = def.allowedValues ?? [];
  return (
    <div>
      <label style={labelStyle}>{label}</label>
      <select
        {...register(def.name)}
        style={{ ...selectStyle, borderColor: error ? "#EF4444" : "#374151" }}
      >
        {options.map((opt) => (
          <option key={opt} value={opt}>
            {capitalize(opt)}
          </option>
        ))}
      </select>
      {error && <p style={errorStyle}>{error}</p>}
    </div>
  );
}

function StringArrayField({ def, label, setValue, value, error, t }: FieldProps) {
  const [draft, setDraft] = useState("");
  const items = Array.isArray(value) ? (value as string[]) : [];
  const allowed = def.allowedValues;
  const atMax = typeof def.maxItems === "number" && items.length >= def.maxItems;

  function addTag(raw: string) {
    const tag = raw.trim();
    if (!tag || items.includes(tag)) {
      setDraft("");
      return;
    }
    if (typeof def.maxItems === "number" && items.length >= def.maxItems) return;
    setValue(def.name, [...items, tag], { shouldValidate: true, shouldDirty: true });
    setDraft("");
  }

  function removeTag(tag: string) {
    setValue(
      def.name,
      items.filter((v) => v !== tag),
      { shouldValidate: true, shouldDirty: true },
    );
  }

  return (
    <div>
      <label style={labelStyle}>
        {label}
        <RequiredMark required={def.required} />
      </label>

      {allowed && allowed.length > 0 ? (
        <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
          {allowed.map((opt) => {
            const checked = items.includes(opt);
            const disabled = !checked && atMax;
            return (
              <button
                key={opt}
                type="button"
                disabled={disabled}
                onClick={() => (checked ? removeTag(opt) : addTag(opt))}
                style={{
                  padding: "6px 12px",
                  borderRadius: 999,
                  fontSize: 12,
                  fontWeight: 600,
                  cursor: disabled ? "not-allowed" : "pointer",
                  border: checked ? "1px solid #3B82F6" : "1px solid #374151",
                  background: checked ? "#1D3461" : "transparent",
                  color: checked ? "#93C5FD" : "#9CA3AF",
                  opacity: disabled ? 0.5 : 1,
                }}
              >
                {capitalize(opt)}
              </button>
            );
          })}
        </div>
      ) : (
        <>
          {items.length > 0 && (
            <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginBottom: 8 }}>
              {items.map((tag) => (
                <span
                  key={tag}
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    gap: 6,
                    padding: "4px 8px",
                    borderRadius: 6,
                    background: "#161B26",
                    border: "1px solid #374151",
                    color: "#E8EDF5",
                    fontSize: 12,
                  }}
                >
                  {tag}
                  <button
                    type="button"
                    onClick={() => removeTag(tag)}
                    aria-label={t("removeTag")}
                    style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 0, display: "flex" }}
                  >
                    <X size={12} />
                  </button>
                </span>
              ))}
            </div>
          )}
          <input
            value={draft}
            disabled={atMax}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === ",") {
                e.preventDefault();
                addTag(draft);
              }
            }}
            placeholder={atMax ? undefined : t("addTagPlaceholder")}
            style={{ ...inputStyle, borderColor: error ? "#EF4444" : "#374151", opacity: atMax ? 0.6 : 1 }}
          />
        </>
      )}

      {error && <p style={errorStyle}>{error}</p>}
      {!error && (def.minItems != null || def.maxItems != null) && (
        <p style={hintStyle}>{t("itemsHint", { min: def.minItems ?? 0, max: def.maxItems ?? "∞" })}</p>
      )}
    </div>
  );
}

/** Dispatch on `def.type` only — the six `BlockPropTypes.cs` cases. This is the sole switch in
 *  the file; there is deliberately no branch anywhere on the block's own `type`. */
function PropField(props: FieldProps) {
  switch (props.def.type) {
    case "bool":
      return <BoolField {...props} />;
    case "enum":
      return <EnumField {...props} />;
    case "stringArray":
      return <StringArrayField {...props} />;
    case "int":
      return <NumberField {...props} />;
    case "url":
      return <TextField {...props} isUrl />;
    case "string":
    default:
      return <TextField {...props} />;
  }
}

// ── Editor drawer ────────────────────────────────────────────────────────────────────────────

interface BlockPropertyEditorProps {
  block: MobileConfigBlockInstance;
  definition: BlockDefinitionDto | undefined;
  onClose: () => void;
  onApply: (props: Record<string, unknown>) => void;
  /**
   * TASK-565: fired on every keystroke/toggle, before "Apply" — mirrors `ThemeEditorSection.tsx`'s
   * own existing `watch()`-based live-preview pattern. Lets `AppBuilderCanvas` reflect unsaved
   * edits in the preview column instantly. Does not change this component's own persistence
   * behavior — "Apply" (`onApply`) is still the only thing that writes into `configDoc`.
   */
  onLiveChange?: (props: Record<string, unknown>) => void;
}

/**
 * TASK-540: property editor for a single block selected on the App Builder canvas (TASK-539).
 * Entirely schema-driven off `definition.validationSchema` (`BlockDefinitionDto.validationSchema`,
 * TASK-538's `GET /api/v1/mobile/blocks`) — a new block type registered on the backend renders
 * here with zero changes to this file, as long as its props use the six existing
 * `BlockPropTypes.cs` kinds.
 *
 * "Apply" only writes the edited props into `AppBuilderCanvas`'s in-memory `configDoc` (via
 * `onApply`) — it does not call the backend. Persisting still goes through the canvas's own
 * explicit "Save draft" button (TASK-538b's Draft CRUD `PUT`), matching this feature's
 * established explicit-save convention (no autosave anywhere in this directory).
 */
export function BlockPropertyEditor({ block, definition, onClose, onApply, onLiveChange }: BlockPropertyEditorProps) {
  const t = useTranslations("Dashboard.consumerApp.appBuilder.propertyEditor");
  const tCommon = useTranslations("Common");

  const schemaDefs = useMemo(() => definition?.validationSchema ?? [], [definition]);
  const zodSchema = useMemo(() => buildPropsSchema(schemaDefs, t), [schemaDefs, t]);
  const defaultValues = useMemo(() => buildDefaultValues(schemaDefs, block.props), [schemaDefs, block.props]);

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors },
  } = useForm<PropFormValues>({
    resolver: zodResolver(zodSchema),
    defaultValues,
  });

  const values = watch();

  // TASK-565b (fix for TASK-566's regression): `watch()` called during render (above, still used
  // to drive this form's own controlled inputs, e.g. `StringArrayField`'s `value` prop) returns a
  // NEW object reference every render — react-hook-form doesn't memoize it. Keying a `useEffect`
  // off that unstable reference (the previous implementation) fired the effect on every render,
  // which called `onLiveChange` → parent `setLiveProps` → re-render → new `values` reference →
  // effect fires again: an infinite "Maximum update depth exceeded" loop for as long as this
  // component was mounted. `watch`'s callback/subscription form has a stable function identity and
  // only invokes the callback when a field actually changes, so it's safe as an effect dependency.
  useEffect(() => {
    const subscription = watch((formValues) => {
      onLiveChange?.(formValues as Record<string, unknown>);
    });
    return () => subscription.unsubscribe();
  }, [watch, onLiveChange]);

  function onValid(formValues: PropFormValues) {
    onApply(formValues);
  }

  return (
    <DetailDrawer
      isOpen
      onClose={onClose}
      title={definition?.displayName ?? block.type}
      subtitle={t("subtitle")}
      width={420}
    >
      {!definition ? (
        <p style={{ color: "#9CA3AF", fontSize: 13 }}>{t("unknownBlockType")}</p>
      ) : (
        <form onSubmit={handleSubmit(onValid)}>
          {definition.supportedDataSource && (
            <div
              style={{
                display: "flex",
                alignItems: "flex-start",
                gap: 8,
                padding: "10px 12px",
                background: "#1F2937",
                borderRadius: 8,
                marginBottom: schemaDefs.length > 0 ? 4 : 0,
              }}
            >
              <Info size={15} style={{ color: "#3B82F6", flexShrink: 0, marginTop: 1 }} />
              <p style={{ color: "#D1D5DB", fontSize: 12, margin: 0 }}>{definition.supportedDataSource}</p>
            </div>
          )}

          {schemaDefs.length === 0 ? (
            <p style={{ ...hintStyle, marginTop: 16 }}>{t("noProps")}</p>
          ) : (
            schemaDefs.map((def) => (
              <div key={def.name} style={fieldWrapStyle}>
                <PropField
                  def={def}
                  label={humanizeFieldName(def.name)}
                  register={register}
                  setValue={setValue}
                  value={values[def.name]}
                  error={errors[def.name]?.message as string | undefined}
                  t={t}
                />
              </div>
            ))
          )}

          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: 10,
              marginTop: 20,
              paddingTop: 16,
              borderTop: "1px solid #1F2937",
            }}
          >
            <Btn type="submit">{t("applyButton")}</Btn>
            <Btn variant="ghost" type="button" onClick={onClose}>
              {tCommon("cancel")}
            </Btn>
          </div>
        </form>
      )}
    </DetailDrawer>
  );
}
