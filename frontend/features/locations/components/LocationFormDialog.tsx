"use client";

import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Btn } from "@/components/ui/Btn";
import { useModules } from "@/features/modules/hooks/useModules";
import { useLegalEntities } from "@/features/legal-entities/hooks/useLegalEntities";
import { LOCATION_TYPE_LABELS, type LocationDto, type LocationType } from "../types";

// ── Schema ─────────────────────────────────────────────────────────────────────

const schema = z.object({
  name: z.string().min(1, "Назва обов'язкова").max(255),
  address: z.string().max(500).optional(),
  locationType: z.enum([
    "retail_store",
    "warehouse",
    "auto_service",
    "office",
    "production",
    "restaurant",
  ] as const),
  isActive: z.boolean(),
  legalEntityId: z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

// ── Props ──────────────────────────────────────────────────────────────────────

interface Props {
  /** null = create mode */
  location: LocationDto | null;
  isPending: boolean;
  onClose: () => void;
  onSubmit: (values: {
    name: string;
    address: string | null;
    locationType: LocationType;
    isActive: boolean;
    legalEntityId: string | null;
  }) => void;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

/** Which location types make sense for each business type */
const TYPES_FOR_BUSINESS: Record<string, LocationType[]> = {
  retail:       ["retail_store", "warehouse"],
  auto_service: ["auto_service", "office", "warehouse"],
  restaurant:   ["restaurant", "warehouse", "office"],
  production:   ["production", "warehouse", "office"],
  warehouse:    ["warehouse"],
  distribution: ["warehouse"],
};

// ── Component ──────────────────────────────────────────────────────────────────

export function LocationFormDialog({ location, isPending, onClose, onSubmit }: Props) {
  const isEdit = location !== null;

  const { data: modules } = useModules();
  const businessType = modules?.businessType ?? "retail";
  const allowedTypes = TYPES_FOR_BUSINESS[businessType] ?? (Object.keys(LOCATION_TYPE_LABELS) as LocationType[]);
  const locationTypes = allowedTypes.map((k) => [k, LOCATION_TYPE_LABELS[k]] as [LocationType, string]);

  const { data: legalEntities } = useLegalEntities();
  const activeLegalEntities = (legalEntities ?? []).filter((e) => e.isActive);

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      address: "",
      locationType: "retail_store",
      isActive: true,
      legalEntityId: "",
    },
  });

  // Populate form when editing
  useEffect(() => {
    if (location) {
      reset({
        name: location.name,
        address: location.address ?? "",
        locationType: location.locationType,
        isActive: location.isActive,
        legalEntityId: location.legalEntityId ?? "",
      });
    } else {
      reset({
        name: "",
        address: "",
        locationType: "retail_store",
        isActive: true,
        legalEntityId: "",
      });
    }
  }, [location, reset]);

  function onValid(values: FormValues) {
    onSubmit({
      name: values.name,
      address: values.address?.trim() || null,
      locationType: values.locationType,
      isActive: values.isActive,
      legalEntityId: values.legalEntityId || null,
    });
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.55)",
        zIndex: 50,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
      }}
      onClick={onClose}
    >
      <div
        style={{
          background: "#161B26",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "24px 28px",
          width: 460,
          maxWidth: "90vw",
          display: "flex",
          flexDirection: "column",
          gap: 18,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Title */}
        <h2 style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, margin: 0 }}>
          {isEdit ? "Редагувати локацію" : "Нова локація"}
        </h2>

        <form
          onSubmit={handleSubmit(onValid)}
          style={{ display: "flex", flexDirection: "column", gap: 14 }}
        >
          {/* Name */}
          <Field label="Назва" error={errors.name?.message}>
            <input
              {...register("name")}
              placeholder="Наприклад: Магазин №1"
              style={inputStyle}
            />
          </Field>

          {/* Location type */}
          <Field label="Тип локації" error={errors.locationType?.message}>
            <select {...register("locationType")} style={inputStyle}>
              {locationTypes.map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </Field>

          {/* Address */}
          <Field label="Адреса (необов'язково)" error={errors.address?.message}>
            <input
              {...register("address")}
              placeholder="вул. Шевченка, 1, Київ"
              style={inputStyle}
            />
          </Field>

          {/* Legal entity */}
          <Field label="Юридична особа (необов'язково)" error={errors.legalEntityId?.message}>
            <select {...register("legalEntityId")} style={inputStyle}>
              <option value="">— Не вказано —</option>
              {activeLegalEntities.map((entity) => (
                <option key={entity.id} value={entity.id}>
                  {entity.legalName}
                </option>
              ))}
            </select>
          </Field>

          {/* Active */}
          {isEdit && (
            <label
              style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer", color: "#9CA3AF", fontSize: 13 }}
            >
              <input type="checkbox" {...register("isActive")} />
              Активна
            </label>
          )}

          {/* Buttons */}
          <div style={{ display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 4 }}>
            <Btn variant="ghost" type="button" onClick={onClose}>
              Скасувати
            </Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? "Збереження…" : isEdit ? "Зберегти" : "Створити"}
            </Btn>
          </div>
        </form>
      </div>
    </div>
  );
}

// ── Shared styles ──────────────────────────────────────────────────────────────

function Field({
  label,
  error,
  children,
}: {
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
      <label style={{ color: "#9CA3AF", fontSize: 12 }}>{label}</label>
      {children}
      {error && <span style={{ color: "#ef4444", fontSize: 11 }}>{error}</span>}
    </div>
  );
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 12px",
  outline: "none",
  boxSizing: "border-box",
};
