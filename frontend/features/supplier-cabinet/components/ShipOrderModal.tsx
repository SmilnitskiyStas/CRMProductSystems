"use client";

// Supplier "Ship" modal (TASK-584 → supplier-portal expansion Phase 3, plan D4).
//
// Two modes, chosen by the provider-granted "supplier_inventory" module:
//   • module OFF  — the original flow: one positive-integer "days to delivery" input,
//     POST /orders/{id}/status {status:"shipped", estimatedDeliveryDays}. Nothing is
//     consumed from stock (there is no supplier stock model when the module is off).
//   • module ON   — pick a source warehouse, review / edit the FEFO batch allocation the
//     backend proposes per line, set an expected delivery date OR a day count, then
//     POST /orders/{id}/ship. That consumes the chosen supplier_stock batches and hands
//     them to the client's receiving draft as prefilled sub-rows.
//
// Routing rule (backend task log 683): with the module ON the legacy /status endpoint
// still "ships" but WITHOUT consuming stock, so the ship action MUST go to /ship here.

import { useEffect, useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import { X } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { useModules } from "@/features/modules/hooks/useModules";
import type { MarketplaceOrderDto } from "@/features/marketplace/types";
import { useSupplierWarehouses } from "../hooks/useSupplierWarehouses";
import { useShipSuggestion, useShipOrder, useUpdateCabinetOrderStatus } from "../hooks/useCabinetCooperation";
import type { ShipLine } from "../types";

interface Props {
  order: MarketplaceOrderDto;
  onClose: () => void;
}

const overlayStyle: React.CSSProperties = {
  position: "fixed",
  inset: 0,
  background: "rgba(0,0,0,0.6)",
  display: "flex",
  alignItems: "center",
  justifyContent: "center",
  zIndex: 999,
  padding: 20,
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  boxSizing: "border-box",
  background: "#1F2937",
  border: "1px solid #374151",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "9px 12px",
  outline: "none",
  fontFamily: "inherit",
};

const compactInputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0A0F1A",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 12,
  padding: "6px 8px",
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  marginBottom: 6,
};

const selectStyle: React.CSSProperties = {
  ...inputStyle,
  appearance: "auto",
};

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

/** "YYYY-MM-DD" → localized date, TZ-noon-guarded so it never rolls to the day before. */
function fmtDate(iso: string, locale: string): string {
  return new Date(`${iso}T12:00:00`).toLocaleDateString(locale);
}

export function ShipOrderModal({ order, onClose }: Props) {
  const t = useTranslations("Dashboard.supplierCabinet.ordersTab");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const { data: modules, isLoading: modulesLoading } = useModules();
  const inventoryOn = modules?.modules?.includes("supplier_inventory") ?? false;

  const title = t("shipModalTitle", { number: order.orderNumber });

  return (
    <div style={overlayStyle} onClick={onClose}>
      <div
        style={{
          background: "#0F1623",
          border: "1px solid #1F2937",
          borderRadius: 12,
          width: "100%",
          maxWidth: inventoryOn ? 640 : 440,
          maxHeight: "85vh",
          overflowY: "auto",
          padding: 20,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div
          style={{
            display: "flex",
            alignItems: "center",
            justifyContent: "space-between",
            marginBottom: 14,
          }}
        >
          <h3 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>{title}</h3>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", cursor: "pointer", color: "#4B5563", padding: 4 }}
          >
            <X size={18} />
          </button>
        </div>

        {modulesLoading ? (
          <div style={{ color: "#6B7280", fontSize: 13, padding: "12px 0" }}>{t("loading")}</div>
        ) : inventoryOn ? (
          <BatchShipForm
            order={order}
            onClose={onClose}
            intlLocale={intlLocale}
            t={t}
            tCommon={tCommon}
          />
        ) : (
          <SimpleShipForm order={order} onClose={onClose} t={t} tCommon={tCommon} />
        )}
      </div>
    </div>
  );
}

// ─── module OFF — the original days-only flow ─────────────────────────────────

function SimpleShipForm({
  order,
  onClose,
  t,
  tCommon,
}: {
  order: MarketplaceOrderDto;
  onClose: () => void;
  t: ReturnType<typeof useTranslations>;
  tCommon: ReturnType<typeof useTranslations>;
}) {
  const updateStatus = useUpdateCabinetOrderStatus();
  const [value, setValue] = useState("");

  const parsed = Number(value);
  const isValid = value.trim() !== "" && Number.isInteger(parsed) && parsed > 0;

  function submit() {
    updateStatus.mutate(
      { id: order.id, body: { status: "shipped", estimatedDeliveryDays: parsed } },
      {
        onSuccess: () => {
          toast.success(t("toastUpdated", { number: order.orderNumber }));
          onClose();
        },
        onError: (err) => toast.error(err.message),
      },
    );
  }

  return (
    <>
      <label style={labelStyle}>
        {t("shipModalLabel")}
        <span style={{ color: "#F87171" }}> *</span>
      </label>
      <input
        type="number"
        inputMode="numeric"
        min={1}
        step={1}
        value={value}
        onChange={(e) => setValue(e.target.value)}
        placeholder={t("shipModalPlaceholder")}
        style={inputStyle}
      />

      <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 16 }}>
        <Btn variant="ghost" onClick={onClose}>
          {tCommon("cancel")}
        </Btn>
        <Btn
          variant="primary"
          disabled={updateStatus.isPending || !isValid}
          onClick={submit}
        >
          {updateStatus.isPending ? t("shipModalPending") : t("shipModalConfirm")}
        </Btn>
      </div>
    </>
  );
}

// ─── module ON — warehouse + editable FEFO allocation ─────────────────────────

const allocKey = (orderItemId: string, supplierStockId: string) => `${orderItemId}|${supplierStockId}`;

function BatchShipForm({
  order,
  onClose,
  intlLocale,
  t,
  tCommon,
}: {
  order: MarketplaceOrderDto;
  onClose: () => void;
  intlLocale: string;
  t: ReturnType<typeof useTranslations>;
  tCommon: ReturnType<typeof useTranslations>;
}) {
  const { data: warehouses = [], isLoading: warehousesLoading } = useSupplierWarehouses();
  const activeWarehouses = useMemo(() => warehouses.filter((w) => w.isActive), [warehouses]);

  const [warehouseId, setWarehouseId] = useState<string | null>(null);
  const effectiveWarehouseId = warehouseId ?? activeWarehouses[0]?.id ?? null;
  const hasWarehouse = activeWarehouses.length > 0;

  const { data: suggestion, isLoading: suggestionLoading, isError: suggestionError } =
    useShipSuggestion(order.id, hasWarehouse ? effectiveWarehouseId : null);

  const shipOrder = useShipOrder();

  // Editable per-allocation quantities, keyed by orderItemId|supplierStockId.
  const [qty, setQty] = useState<Record<string, string>>({});
  useEffect(() => {
    if (!suggestion) return;
    const next: Record<string, string> = {};
    for (const line of suggestion.lines) {
      for (const a of line.allocations) {
        next[allocKey(line.orderItemId, a.supplierStockId)] = String(a.qty);
      }
    }
    setQty(next);
  }, [suggestion]);

  const [days, setDays] = useState("");
  const [date, setDate] = useState("");
  const [formError, setFormError] = useState<string | null>(null);

  const daysNum = Number(days);
  const daysValid = days.trim() !== "" && Number.isInteger(daysNum) && daysNum > 0;
  const etaProvided = daysValid || date.trim() !== "";

  const derivedDate = useMemo(() => {
    if (!daysValid || date.trim() !== "") return null;
    const d = new Date();
    d.setDate(d.getDate() + daysNum);
    return d.toLocaleDateString(intlLocale);
  }, [daysValid, daysNum, date, intlLocale]);

  function lineCovered(line: { orderItemId: string; allocations: { supplierStockId: string }[] }): number {
    return line.allocations.reduce((sum, a) => {
      const raw = qty[allocKey(line.orderItemId, a.supplierStockId)];
      const n = parseFloat(raw ?? "");
      return sum + (Number.isFinite(n) && n > 0 ? n : 0);
    }, 0);
  }

  function submit() {
    setFormError(null);
    if (!etaProvided) {
      setFormError(t("shipModalEtaRequired"));
      return;
    }

    const lines: ShipLine[] = (suggestion?.lines ?? [])
      .map((line) => ({
        orderItemId: line.orderItemId,
        allocations: line.allocations
          .map((a) => ({
            supplierStockId: a.supplierStockId,
            qty: parseFloat(qty[allocKey(line.orderItemId, a.supplierStockId)] ?? "") || 0,
          }))
          .filter((a) => a.qty > 0),
      }))
      .filter((line) => line.allocations.length > 0);

    shipOrder.mutate(
      {
        id: order.id,
        body: {
          sourceWarehouseId: hasWarehouse ? effectiveWarehouseId : undefined,
          expectedDeliveryDate: date.trim() || undefined,
          estimatedDeliveryDays: daysValid ? daysNum : undefined,
          lines: lines.length > 0 ? lines : undefined,
        },
      },
      {
        onSuccess: (result) => {
          toast.success(t("toastUpdated", { number: order.orderNumber }));
          // A shortfall is allowed by design — surface each backend warning as a plain
          // (non-error) toast; the ship already succeeded.
          for (const w of result.warnings) toast.warning(w);
          onClose();
        },
        onError: (err) => setFormError(err.message),
      },
    );
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      {/* Warehouse */}
      {hasWarehouse ? (
        <div>
          <label style={labelStyle}>{t("shipModalWarehouseLabel")}</label>
          <select
            value={effectiveWarehouseId ?? ""}
            onChange={(e) => setWarehouseId(e.target.value || null)}
            style={selectStyle}
            disabled={warehousesLoading}
          >
            {activeWarehouses.map((w) => (
              <option key={w.id} value={w.id}>
                {w.name}
              </option>
            ))}
          </select>
        </div>
      ) : (
        <div style={{ color: "#FBBF24", fontSize: 12 }}>{t("shipModalNoWarehouseHint")}</div>
      )}

      {/* Per-line allocation */}
      {hasWarehouse && suggestionLoading && (
        <div style={{ color: "#6B7280", fontSize: 12 }}>{t("shipModalSuggestionLoading")}</div>
      )}
      {hasWarehouse && suggestionError && (
        <div style={{ color: "#F87171", fontSize: 12 }}>{t("shipModalSuggestionError")}</div>
      )}
      {hasWarehouse &&
        suggestion?.lines.map((line) => {
          const covered = lineCovered(line);
          const shortfall = Math.max(0, line.qty - covered);
          return (
            <div
              key={line.orderItemId}
              style={{
                background: "#111827",
                border: "1px solid #1F2937",
                borderRadius: 8,
                padding: "10px 12px",
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "baseline",
                  justifyContent: "space-between",
                  gap: 10,
                  marginBottom: 8,
                }}
              >
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                  {line.itemName}
                  {line.unit && <span style={{ color: "#4B5563", fontSize: 11 }}> · {line.unit}</span>}
                </span>
                <span style={{ color: "#9CA3AF", fontSize: 11, whiteSpace: "nowrap" }}>
                  {t("shipModalLineOrdered", { qty: line.qty })}
                </span>
              </div>

              {line.allocations.length === 0 ? (
                <div style={{ color: "#6B7280", fontSize: 11 }}>{t("shipModalNoBatches")}</div>
              ) : (
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "1fr 1fr 0.7fr 0.8fr",
                    gap: 6,
                    fontSize: 11,
                  }}
                >
                  <span style={{ color: "#6B7280" }}>{t("shipModalAllocExpiry")}</span>
                  <span style={{ color: "#6B7280" }}>{t("shipModalAllocBatch")}</span>
                  <span style={{ color: "#6B7280", textAlign: "right" }}>{t("shipModalAllocAvailable")}</span>
                  <span style={{ color: "#6B7280" }}>{t("shipModalAllocQty")}</span>
                  {line.allocations.map((a) => (
                    <AllocationRow
                      key={a.supplierStockId}
                      expiry={fmtDate(a.expiryDate, intlLocale)}
                      batchNumber={a.batchNumber}
                      available={a.available}
                      value={qty[allocKey(line.orderItemId, a.supplierStockId)] ?? ""}
                      onChange={(v) =>
                        setQty((prev) => ({ ...prev, [allocKey(line.orderItemId, a.supplierStockId)]: v }))
                      }
                    />
                  ))}
                </div>
              )}

              {shortfall > 0 && (
                <div
                  style={{
                    marginTop: 8,
                    display: "inline-block",
                    background: "#3A2A0A",
                    border: "1px solid #92610E",
                    borderRadius: 6,
                    color: "#FBBF24",
                    fontSize: 11,
                    padding: "3px 8px",
                  }}
                >
                  {t("shipModalShortfallChip", { covered, qty: line.qty })}
                </div>
              )}
            </div>
          );
        })}

      {/* Delivery estimate — one of date / days required */}
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
        <div>
          <label style={labelStyle}>{t("shipModalExpectedDateLabel")}</label>
          <input
            type="date"
            min={todayIso()}
            value={date}
            onChange={(e) => setDate(e.target.value)}
            style={inputStyle}
          />
        </div>
        <div>
          <label style={labelStyle}>{t("shipModalLabel")}</label>
          <input
            type="number"
            inputMode="numeric"
            min={1}
            step={1}
            value={days}
            onChange={(e) => setDays(e.target.value)}
            placeholder={t("shipModalPlaceholder")}
            style={inputStyle}
          />
        </div>
      </div>
      <div style={{ color: "#6B7280", fontSize: 11, marginTop: -4 }}>
        {derivedDate ? t("shipModalDerivedDateHint", { date: derivedDate }) : t("shipModalEtaHint")}
      </div>

      {formError && <div style={{ color: "#F87171", fontSize: 12 }}>{formError}</div>}

      <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 4 }}>
        <Btn variant="ghost" onClick={onClose}>
          {tCommon("cancel")}
        </Btn>
        <Btn
          variant="primary"
          disabled={shipOrder.isPending || !etaProvided || (hasWarehouse && suggestionLoading)}
          onClick={submit}
        >
          {shipOrder.isPending ? t("shipModalPending") : t("shipModalShipButton")}
        </Btn>
      </div>
    </div>
  );
}

function AllocationRow({
  expiry,
  batchNumber,
  available,
  value,
  onChange,
}: {
  expiry: string;
  batchNumber: string | null;
  available: number;
  value: string;
  onChange: (v: string) => void;
}) {
  return (
    <>
      <span style={{ color: "#E8EDF5" }}>{expiry}</span>
      <span style={{ color: "#9CA3AF" }}>{batchNumber || "—"}</span>
      <span style={{ color: "#9CA3AF", textAlign: "right" }}>{available}</span>
      <input
        type="number"
        step="any"
        min={0}
        max={available}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        style={compactInputStyle}
      />
    </>
  );
}
