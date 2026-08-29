"use client";

import { useState } from "react";
import { Plus, ChevronDown, ChevronRight, Pencil, Trash2 } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useCustomers, useDeleteCustomer, useVehicles } from "../hooks/useAutoService";
import { CustomerForm } from "./CustomerForm";
import { VehicleForm } from "./VehicleForm";
import type { CustomerDto } from "../types";
import { Table, type TableColumn } from "@/components/ui/Table";

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

  // Column 0 is the expand toggle (a leading utility column, structurally like the checkbox
  // column in shelf/StockTable.tsx), so the real "label" column (customer name) sits at index 1
  // and needs an explicit `align: "left"` override — the default would otherwise center it.
  const columns: TableColumn<CustomerDto>[] = [
    {
      key: "expand",
      width: 32,
      align: "center",
      header: "",
      render: (customer) =>
        expandedId === customer.id ? <ChevronDown size={14} /> : <ChevronRight size={14} />,
      cellStyle: { color: "#4B5563" },
    },
    {
      key: "name",
      align: "left",
      header: t("headerCustomer"),
      render: (customer) => (
        <div>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{customer.name}</div>
          {customer.notes && (
            <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>{customer.notes}</div>
          )}
        </div>
      ),
    },
    {
      key: "phone",
      header: t("headerPhone"),
      render: (customer) => customer.phone ?? "—",
    },
    {
      key: "email",
      header: t("headerEmail"),
      render: (customer) => customer.email ?? "—",
    },
    {
      key: "vehicles",
      header: t("headerVehicles"),
      render: (customer) => customer.vehicleCount,
    },
    {
      key: "actions",
      header: "",
      render: (customer) => (
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
      ),
    },
  ];

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
      <Table
        columns={columns}
        rows={customers ?? []}
        rowKey={(customer) => customer.id}
        onRowClick={(customer) =>
          setExpandedId((prev) => (prev === customer.id ? null : customer.id))
        }
        expandedRowKey={expandedId}
        renderExpanded={(customer) => <CustomerVehicleList customer={customer} />}
        emptyMessage={t("empty")}
      />

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
