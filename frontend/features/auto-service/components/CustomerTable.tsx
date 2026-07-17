"use client";

import { useState } from "react";
import { Plus, ChevronDown, ChevronRight, Pencil, Trash2 } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useCustomers, useDeleteCustomer, useVehicles } from "../hooks/useAutoService";
import { CustomerForm } from "./CustomerForm";
import { VehicleForm } from "./VehicleForm";
import type { CustomerDto } from "../types";

// ─── Vehicle sub-row ──────────────────────────────────────────────────────────

function CustomerVehicleList({ customer }: { customer: CustomerDto }) {
  const t = useTranslations("Dashboard.autoService.customerTable");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: vehicles, isLoading } = useVehicles(customer.id);
  const [showVehicleForm, setShowVehicleForm] = useState(false);

  return (
    <div
      style={{
        padding: "14px 20px 14px 48px",
        background: "#0D1117",
        borderTop: "1px solid #1F2937",
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 10 }}>
        <span style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, textTransform: "uppercase" }}>
          {t("vehiclesLabel")}
        </span>
        <button
          onClick={() => setShowVehicleForm(true)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 4,
            padding: "4px 10px",
            borderRadius: 6,
            border: "1px solid #1F2937",
            background: "transparent",
            color: "#9CA3AF",
            fontSize: 11,
            cursor: "pointer",
          }}
        >
          <Plus size={12} /> {t("addVehicle")}
        </button>
      </div>

      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 12 }}>{tCommon("loading")}</div>
      ) : !vehicles || vehicles.length === 0 ? (
        <div style={{ color: "#374151", fontSize: 12 }}>{t("noVehicles")}</div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
          {vehicles.map((v) => (
            <div
              key={v.id}
              style={{
                display: "flex",
                alignItems: "center",
                gap: 12,
                padding: "8px 12px",
                background: "#111827",
                border: "1px solid #1F2937",
                borderRadius: 8,
              }}
            >
              <span style={{ fontSize: 16 }}>🚗</span>
              <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                {v.brand} {v.model}
              </span>
              {v.year && <span style={{ color: "#6B7280", fontSize: 12 }}>{v.year}</span>}
              <span style={{ color: "#60A5FA", fontSize: 12, fontWeight: 600 }}>
                {v.licensePlate}
              </span>
              {v.vin && <span style={{ color: "#4B5563", fontSize: 11 }}>{t("vin")}: {v.vin}</span>}
              {v.mileage != null && (
                <span style={{ color: "#4B5563", fontSize: 11 }}>
                  {v.mileage.toLocaleString(intlLocale)} {t("mileageUnit")}
                </span>
              )}
            </div>
          ))}
        </div>
      )}

      {showVehicleForm && (
        <VehicleForm customerId={customer.id} onClose={() => setShowVehicleForm(false)} />
      )}
    </div>
  );
}

// ─── Main CustomerTable ───────────────────────────────────────────────────────

export function CustomerTable() {
  const t = useTranslations("Dashboard.autoService.customerTable");
  const tCommon = useTranslations("Common");
  const { data: customers, isLoading, isError } = useCustomers();
  const deleteCustomer = useDeleteCustomer();

  const [showCreate, setShowCreate] = useState(false);
  const [editingCustomer, setEditingCustomer] = useState<CustomerDto | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);

  if (isLoading) {
    return <div style={{ padding: "32px", color: "#6B7280", fontSize: 13 }}>{tCommon("loading")}</div>;
  }
  if (isError) {
    return <div style={{ padding: "32px", color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>;
  }

  return (
    <>
      {/* Toolbar */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
          <p style={{ color: "#4B5563", fontSize: 14, marginTop: 4 }}>
            {t("subtitle")}
          </p>
        </div>
        <button
          onClick={() => setShowCreate(true)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            padding: "9px 16px",
            borderRadius: 8,
            border: "none",
            background: "#1D4ED8",
            color: "#E8EDF5",
            fontSize: 13,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          <Plus size={16} /> {t("addCustomer")}
        </button>
      </div>

      {/* Table */}
      <div style={{ background: "#111827", border: "1px solid #1F2937", borderRadius: 10, overflow: "hidden" }}>
        {/* Header */}
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "32px 1fr 160px 160px 80px 80px",
            padding: "10px 16px",
            borderBottom: "1px solid #1F2937",
          }}
        >
          {["", t("headerCustomer"), t("headerPhone"), t("headerEmail"), t("headerVehicles"), ""].map((h, i) => (
            <div key={i} style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase" }}>
              {h}
            </div>
          ))}
        </div>

        {/* Rows */}
        {!customers || customers.length === 0 ? (
          <div style={{ padding: "40px 16px", textAlign: "center", color: "#374151", fontSize: 13 }}>
            {t("empty")}
          </div>
        ) : (
          customers.map((customer) => {
            const expanded = expandedId === customer.id;
            return (
              <div key={customer.id} style={{ borderBottom: "1px solid #1F2937" }}>
                {/* Row */}
                <div
                  style={{
                    display: "grid",
                    gridTemplateColumns: "32px 1fr 160px 160px 80px 80px",
                    padding: "12px 16px",
                    alignItems: "center",
                    cursor: "pointer",
                    transition: "background 0.1s",
                  }}
                  onClick={() => setExpandedId(expanded ? null : customer.id)}
                  onMouseEnter={(e) => { (e.currentTarget as HTMLElement).style.background = "#0D1117"; }}
                  onMouseLeave={(e) => { (e.currentTarget as HTMLElement).style.background = "transparent"; }}
                >
                  {/* Expand toggle */}
                  <div style={{ color: "#4B5563" }}>
                    {expanded
                      ? <ChevronDown size={14} />
                      : <ChevronRight size={14} />
                    }
                  </div>
                  {/* Name */}
                  <div>
                    <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{customer.name}</div>
                    {customer.notes && (
                      <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>{customer.notes}</div>
                    )}
                  </div>
                  {/* Phone */}
                  <div style={{ color: "#9CA3AF", fontSize: 13 }}>{customer.phone ?? "—"}</div>
                  {/* Email */}
                  <div style={{ color: "#9CA3AF", fontSize: 13 }}>{customer.email ?? "—"}</div>
                  {/* Vehicle count */}
                  <div style={{ color: "#9CA3AF", fontSize: 13 }}>{customer.vehicleCount}</div>
                  {/* Actions */}
                  <div
                    style={{ display: "flex", gap: 6, justifyContent: "flex-end" }}
                    onClick={(e) => e.stopPropagation()}
                  >
                    <button
                      onClick={() => setEditingCustomer(customer)}
                      style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: "4px 6px" }}
                      title={t("editTitle")}
                    >
                      <Pencil size={13} />
                    </button>
                    <button
                      onClick={() => {
                        if (confirm(t("deleteConfirm", { name: customer.name }))) {
                          deleteCustomer.mutate(customer.id);
                        }
                      }}
                      style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: "4px 6px" }}
                      title={t("deleteTitle")}
                    >
                      <Trash2 size={13} />
                    </button>
                  </div>
                </div>

                {/* Expanded vehicle list */}
                {expanded && <CustomerVehicleList customer={customer} />}
              </div>
            );
          })
        )}
      </div>

      {/* Modals */}
      {showCreate && <CustomerForm onClose={() => setShowCreate(false)} />}
      {editingCustomer && (
        <CustomerForm
          customer={editingCustomer}
          onClose={() => setEditingCustomer(null)}
        />
      )}
    </>
  );
}
