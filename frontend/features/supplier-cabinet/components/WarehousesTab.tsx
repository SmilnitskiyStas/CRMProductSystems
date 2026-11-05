"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { Pencil, Plus, PowerOff } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";
import { Table, type TableColumn } from "@/components/ui/Table";
import { RegionSelect } from "@/features/geo/components/RegionSelect";
import { useRegionLabel } from "@/features/geo/hooks/useRegions";
import {
  useSupplierWarehouses,
  useCreateWarehouse,
  useUpdateWarehouse,
  useDeactivateWarehouse,
} from "../hooks/useSupplierWarehouses";
import type { SupplierWarehouse } from "../types";

interface FormModalProps {
  /** null = create; otherwise edit that warehouse. */
  warehouse: SupplierWarehouse | null;
  onClose: () => void;
}

function WarehouseFormModal({ warehouse, onClose }: FormModalProps) {
  const t = useTranslations("Dashboard.supplierCabinet.warehousesTab");
  const create = useCreateWarehouse();
  const update = useUpdateWarehouse();
  const isEdit = warehouse !== null;

  const [name, setName] = useState(warehouse?.name ?? "");
  const [address, setAddress] = useState(warehouse?.address ?? "");
  const [regionCode, setRegionCode] = useState<string | null>(warehouse?.regionCode ?? null);
  const [error, setError] = useState<string | null>(null);

  const pending = create.isPending || update.isPending;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) {
      setError(t("validationName"));
      return;
    }
    setError(null);
    try {
      if (isEdit) {
        await update.mutateAsync({
          id: warehouse.id,
          body: {
            name: name.trim(),
            address: address.trim() || null,
            regionCode,
            isActive: warehouse.isActive,
          },
        });
      } else {
        await create.mutateAsync({
          name: name.trim(),
          address: address.trim() || null,
          regionCode,
        });
      }
      onClose();
    } catch (err) {
      setError((err as Error)?.message ?? t("errorDefault"));
    }
  }

  return (
    <Modal title={isEdit ? t("editTitle") : t("createTitle")} onClose={onClose} width={460}>
        <form onSubmit={handleSubmit}>
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            <div>
              <label style={labelStyle}>{t("nameLabel")}</label>
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder={t("namePlaceholder")}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>{t("addressLabel")}</label>
              <input
                value={address}
                onChange={(e) => setAddress(e.target.value)}
                placeholder={t("addressPlaceholder")}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>{t("regionLabel")}</label>
              <RegionSelect
                value={regionCode}
                onChange={setRegionCode}
                placeholder={t("regionPlaceholder")}
              />
            </div>
          </div>

          <div style={{ display: "flex", gap: 10, marginTop: 22 }}>
            <Btn type="submit" disabled={pending} style={{ flex: 1, justifyContent: "center" }}>
              {pending ? t("saving") : isEdit ? t("saveButton") : t("createButton")}
            </Btn>
            <Btn type="button" variant="ghost" onClick={onClose}>
              {t("cancel")}
            </Btn>
          </div>

          {error && (
            <p style={{ color: "#F87171", fontSize: 12, marginTop: 10 }}>{error}</p>
          )}
        </form>
    </Modal>
  );
}

export function WarehousesTab() {
  const t = useTranslations("Dashboard.supplierCabinet.warehousesTab");
  const { data: warehouses, isLoading, isError } = useSupplierWarehouses();
  const regionLabel = useRegionLabel();
  const deactivate = useDeactivateWarehouse();

  const [modalTarget, setModalTarget] = useState<SupplierWarehouse | null | "new">(null);

  async function handleDeactivate(w: SupplierWarehouse) {
    if (!confirm(t("deactivateConfirm", { name: w.name }))) return;
    await deactivate.mutateAsync(w.id);
  }

  const columns: TableColumn<SupplierWarehouse>[] = [
    {
      key: "name",
      header: t("headerName"),
      align: "left",
      cellStyle: { fontWeight: 600 },
      render: (w) => w.name,
    },
    {
      key: "address",
      header: t("headerAddress"),
      cellStyle: { color: "#9CA3AF" },
      render: (w) => w.address || "—",
    },
    {
      key: "region",
      header: t("headerRegion"),
      cellStyle: { color: "#9CA3AF" },
      render: (w) => (w.regionCode ? regionLabel(w.regionCode) : "—"),
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (w) => (
        <span style={{ color: w.isActive ? "#4ADE80" : "#6B7280", fontSize: 12 }}>
          {w.isActive ? t("statusActive") : t("statusInactive")}
        </span>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (w) => (
        <div style={{ display: "flex", gap: 6, justifyContent: "center" }} onClick={(e) => e.stopPropagation()}>
          <Btn size="sm" variant="ghost" icon={<Pencil size={13} />} onClick={() => setModalTarget(w)}>
            {t("editButton")}
          </Btn>
          {w.isActive && (
            <Btn
              size="sm"
              variant="danger"
              icon={<PowerOff size={13} />}
              disabled={deactivate.isPending}
              onClick={() => handleDeactivate(w)}
            >
              {t("deactivateButton")}
            </Btn>
          )}
        </div>
      ),
    },
  ];

  return (
    <div
      style={{
        background: "#111827",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "24px 28px",
        display: "flex",
        flexDirection: "column",
        gap: 16,
      }}
    >
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12, flexWrap: "wrap" }}>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h2>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 4 }}>{t("subtitle")}</p>
        </div>
        <Btn onClick={() => setModalTarget("new")} icon={<Plus size={14} />}>
          {t("addButton")}
        </Btn>
      </div>

      {isError ? (
        <div style={{ color: "#F87171", fontSize: 13 }}>{t("errorLoad")}</div>
      ) : (
        <Table
          columns={columns}
          rows={warehouses ?? []}
          rowKey={(w) => w.id}
          isLoading={isLoading}
          emptyMessage={t("empty")}
        />
      )}

      {modalTarget !== null && (
        <WarehouseFormModal
          warehouse={modalTarget === "new" ? null : modalTarget}
          onClose={() => setModalTarget(null)}
        />
      )}
    </div>
  );
}
