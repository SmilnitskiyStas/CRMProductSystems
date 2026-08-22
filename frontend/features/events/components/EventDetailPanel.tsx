"use client";

import { useMemo } from "react";
import { X } from "lucide-react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { DrawerField, DrawerGrid, DrawerSection } from "@/components/ui/DetailDrawer";
import { Btn } from "@/components/ui/Btn";
import type { StoreDto as Store } from "@/features/stores/types";
import { useProductsByIds } from "@/features/inventory/hooks/useProducts";
import type { Product } from "@/features/inventory/types";
import { EVENT_TYPE_STYLES, getEventTypeLabel, type DemandEvent } from "../types";
import { useAddCoefficient, useRemoveCoefficient, useUpdateCoefficient } from "../hooks/useEvents";
import { EventProductPicker } from "./EventProductPicker";
import { LinkedProductSalesCard } from "./LinkedProductSalesCard";

interface Props {
  event: DemandEvent;
  stores: Store[];
  referenceDateIso: string;
  onEdit: (event: DemandEvent) => void;
  onBack: () => void;
}

const coefficientInputStyle: React.CSSProperties = {
  width: 70,
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 12,
  padding: "4px 8px",
  outline: "none",
};

const removeButtonStyle: React.CSSProperties = {
  background: "transparent",
  border: "none",
  color: "#6B7280",
  cursor: "pointer",
  display: "flex",
  alignItems: "center",
  padding: 4,
  flexShrink: 0,
};

/** Scope label keys — all reused from `eventForm` (tForm), matching how the create/edit form
 * already labels these same three options. Lookup object instead of nested ternaries. */
const SCOPE_LABEL_KEYS: Record<DemandEvent["scope"], string> = {
  network: "scopeNetworkOption",
  store: "scopeStoreOption",
  stores: "scopeStoresOption",
};

export function EventDetailPanel({ event, stores, referenceDateIso, onEdit }: Props) {
  const t = useTranslations("Dashboard.events.dayDetail");
  const tTypes = useTranslations("Dashboard.events.types");
  const tForm = useTranslations("Dashboard.events.eventForm");

  const typeStyle = EVENT_TYPE_STYLES[event.eventType];
  const typeLabel = getEventTypeLabel(tTypes, event.eventType);
  const scopeLabel = tForm(SCOPE_LABEL_KEYS[event.scope]);

  // "store" resolves the single linked store; "stores" resolves + joins each id in
  // `event.storeIds` using the exact same lookup, just mapped over the array; "network" has
  // no store to show.
  const storeNames =
    event.scope === "store"
      ? event.storeId
        ? [stores.find((s) => s.id === event.storeId)?.name ?? event.storeId]
        : []
      : event.scope === "stores"
        ? event.storeIds.map((id) => stores.find((s) => s.id === id)?.name ?? id)
        : [];
  const storeName = storeNames.length > 0 ? storeNames.join(", ") : null;

  // Product-scoped coefficients drive both the "Linked Products" editor and the "Sales
  // Comparison" cards below — one filtered list, reused for both sections.
  const productCoefficients = useMemo(
    () => event.coefficients.filter((c) => c.scopeType === "product"),
    [event.coefficients],
  );
  const productIds = useMemo(
    () => productCoefficients.map((c) => c.scopeId).filter((id): id is string => id != null),
    [productCoefficients],
  );

  const { data: linkedProducts } = useProductsByIds(productIds);
  const productById = useMemo(() => {
    const map = new Map<string, Product>();
    for (const p of linkedProducts ?? []) map.set(p.id, p);
    return map;
  }, [linkedProducts]);

  const addCoef = useAddCoefficient();
  const updateCoef = useUpdateCoefficient();
  const removeCoef = useRemoveCoefficient();

  return (
    <div>
      <DrawerGrid>
        <DrawerField label={t("typeLabel")} value={
          <span style={{
            background: typeStyle.bg, color: typeStyle.color, fontSize: 11,
            borderRadius: 5, padding: "2px 8px", display: "inline-block",
          }}>
            {typeLabel}
          </span>
        } />
        <DrawerField label={t("scopeLabel")} value={scopeLabel} />
        <DrawerField label={t("storeLabel")} value={storeName ?? t("noStoreValue")} />
        <DrawerField label={t("datesLabel")} value={`${event.startsAt} – ${event.endsAt}`} />
        <DrawerField label={t("recurringLabel")} value={event.isRecurring ? t("recurringYes") : t("recurringNo")} />
      </DrawerGrid>

      <DrawerField label={t("notesLabel")} value={event.notes || t("noNotesValue")} />

      <Btn size="sm" onClick={() => onEdit(event)}>{t("editButton")}</Btn>

      <div style={{ marginTop: 20 }}>
        <DrawerSection title={t("linkedProductsTitle")}>
          {productCoefficients.length === 0 && (
            <p style={{ color: "#4B5563", fontSize: 12, marginTop: 0, marginBottom: 10 }}>
              {t("linkedProductsEmpty")}
            </p>
          )}

          {productCoefficients.length > 0 && (
            <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 12 }}>
              {productCoefficients.map((c) => {
                const product = c.scopeId ? productById.get(c.scopeId) : undefined;
                const displayName = product?.name ?? (c.scopeId ? `${c.scopeId.slice(0, 8)}…` : t("unresolvedProduct"));
                return (
                  <div key={c.id} style={{ display: "flex", alignItems: "center", gap: 8 }}>
                    <span
                      style={{
                        color: "#E8EDF5",
                        fontSize: 13,
                        flex: 1,
                        minWidth: 0,
                        overflow: "hidden",
                        textOverflow: "ellipsis",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {displayName}
                    </span>
                    <input
                      type="number"
                      step="0.1"
                      min={0.01}
                      max={10}
                      defaultValue={c.coefficient}
                      aria-label={t("coefficientInputLabel")}
                      onBlur={(e) => {
                        const v = Number(e.target.value);
                        if (v > 0 && v !== c.coefficient) {
                          updateCoef.mutate(
                            { eventId: event.id, coefId: c.id, coefficient: v },
                            {
                              onSuccess: () => toast.success(t("toastCoefficientUpdated")),
                              onError: (err) => toast.error(err.message),
                            },
                          );
                        }
                      }}
                      style={coefficientInputStyle}
                    />
                    <button
                      type="button"
                      title={t("removeProductTooltip")}
                      aria-label={t("removeProductTooltip")}
                      onClick={() =>
                        removeCoef.mutate(
                          { eventId: event.id, coefId: c.id },
                          {
                            onSuccess: () => toast.success(t("toastProductUnlinked")),
                            onError: (err) => toast.error(err.message),
                          },
                        )
                      }
                      style={removeButtonStyle}
                    >
                      <X size={13} />
                    </button>
                  </div>
                );
              })}
            </div>
          )}

          <EventProductPicker
            excludeIds={productIds}
            onPick={(product) =>
              addCoef.mutate(
                { eventId: event.id, payload: { scopeType: "product", scopeId: product.id, coefficient: 1.5 } },
                {
                  onSuccess: () => toast.success(t("toastProductLinked")),
                  onError: (err) => toast.error(err.message),
                },
              )
            }
          />
        </DrawerSection>

        <DrawerSection title={t("salesComparisonTitle")}>
          {productCoefficients.length === 0 ? (
            <p style={{ color: "#4B5563", fontSize: 12, margin: 0 }}>{t("salesComparisonEmpty")}</p>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
              {productCoefficients.map((c) => {
                if (!c.scopeId) return null;
                const product = productById.get(c.scopeId);
                return (
                  <LinkedProductSalesCard
                    key={c.id}
                    productId={c.scopeId}
                    productName={product?.name ?? `${c.scopeId.slice(0, 8)}…`}
                    referenceDateIso={referenceDateIso}
                    event={event}
                    storeId={event.scope === "store" ? (event.storeId ?? undefined) : undefined}
                  />
                );
              })}
            </div>
          )}
        </DrawerSection>
      </div>
    </div>
  );
}
