"use client";

import { useState } from "react";
import { Plus, Pencil, Trash2 } from "lucide-react";
import { useTranslations } from "next-intl";
import {
  useServiceCatalog,
  useCreateServiceItem,
  useUpdateServiceItem,
  useDeleteServiceItem,
} from "../hooks/useAutoService";
import { autoServiceApi } from "../api/auto-service-api";
import { useQueryClient } from "@tanstack/react-query";
import { AUTO_SERVICE_KEYS } from "../hooks/useAutoService";
import { Table, type TableColumn } from "@/components/ui/Table";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatusPill } from "@/components/ui/StatusPill";
import { inputStyle, labelStyle, textareaStyle } from "@/components/ui/fieldStyles";
import type {
  ServiceCatalogItemDto,
  CreateServiceCatalogItemRequest,
  UpdateServiceCatalogItemRequest,
} from "../types";

// ─── Service Item Form ────────────────────────────────────────────────────────

interface FormProps {
  item?: ServiceCatalogItemDto;
  onClose: () => void;
}

function ServiceItemForm({ item, onClose }: FormProps) {
  const t = useTranslations("Dashboard.autoService.serviceForm");
  const createItem = useCreateServiceItem();
  const updateItem = useUpdateServiceItem(item?.id ?? "");

  const [name, setName] = useState(item?.name ?? "");
  const [description, setDescription] = useState(item?.description ?? "");
  const [defaultPrice, setDefaultPrice] = useState<number>(item?.defaultPrice ?? 0);
  const [durationMinutes, setDurationMinutes] = useState<number | "">(
    item?.durationMinutes ?? ""
  );
  const [isActive, setIsActive] = useState(item?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);

  const isEdit = !!item;
  const isPending = createItem.isPending || updateItem.isPending;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) { setError(t("errorNameRequired")); return; }

    if (isEdit) {
      const body: UpdateServiceCatalogItemRequest = {
        name: name.trim(),
        description: description.trim() || undefined,
        defaultPrice,
        durationMinutes: durationMinutes !== "" ? Number(durationMinutes) : undefined,
        isActive,
      };
      updateItem.mutate(body, {
        onSuccess: () => onClose(),
        onError: (err) => setError(err instanceof Error ? err.message : t("errorGeneric")),
      });
    } else {
      const body: CreateServiceCatalogItemRequest = {
        name: name.trim(),
        description: description.trim() || undefined,
        defaultPrice,
        durationMinutes: durationMinutes !== "" ? Number(durationMinutes) : undefined,
      };
      createItem.mutate(body, {
        onSuccess: () => onClose(),
        onError: (err) => setError(err instanceof Error ? err.message : t("errorGeneric")),
      });
    }
  }

  return (
    <Modal title={isEdit ? t("titleEdit") : t("titleNew")} onClose={onClose} width={460}>
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <label style={labelStyle}>{t("nameLabel")}</label>
            <input type="text" value={name} onChange={(e) => setName(e.target.value)} placeholder={t("namePlaceholder")} style={inputStyle} />
          </div>
          <div>
            <label style={labelStyle}>{t("descriptionLabel")}</label>
            <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={2} style={textareaStyle} />
          </div>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <div>
              <label style={labelStyle}>{t("priceLabel")}</label>
              <input
                type="number"
                min={0}
                step={0.01}
                value={defaultPrice}
                onChange={(e) => setDefaultPrice(Number(e.target.value))}
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>{t("durationLabel")}</label>
              <input
                type="number"
                min={1}
                step={1}
                value={durationMinutes}
                onChange={(e) => setDurationMinutes(e.target.value ? Number(e.target.value) : "")}
                placeholder="60"
                style={inputStyle}
              />
            </div>
          </div>
          {isEdit && (
            <label style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                style={{ accentColor: "#3B82F6" }}
              />
              <span style={{ color: "#9CA3AF", fontSize: 13 }}>{t("activeLabel")}</span>
            </label>
          )}

          {error && (
            <div style={{ color: "#F87171", fontSize: 12, padding: "8px 12px", background: "#1F1010", borderRadius: 6 }}>
              {error}
            </div>
          )}

          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 4 }}>
            <Btn variant="ghost" onClick={onClose}>{t("cancel")}</Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? t("saving") : isEdit ? t("save") : t("add")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}

// ─── Deactivate button (separate component to use hook correctly) ─────────────

function DeactivateButton({ item }: { item: ServiceCatalogItemDto }) {
  const t = useTranslations("Dashboard.autoService.serviceCatalog");
  const queryClient = useQueryClient();

  async function handleDeactivate() {
    if (!confirm(t("deactivateConfirm", { name: item.name }))) return;
    await autoServiceApi.updateServiceItem(item.id, {
      name: item.name,
      description: item.description ?? undefined,
      defaultPrice: item.defaultPrice,
      durationMinutes: item.durationMinutes ?? undefined,
      isActive: false,
    });
    queryClient.invalidateQueries({ queryKey: AUTO_SERVICE_KEYS.serviceCatalog });
  }

  if (!item.isActive) return null;

  return (
    <Btn variant="ghost" size="xs" onClick={handleDeactivate} title={t("deactivateTitle")}>
      {t("deactivateButton")}
    </Btn>
  );
}

// ─── Main ServiceCatalogTable ─────────────────────────────────────────────────

export function ServiceCatalogTable() {
  const t = useTranslations("Dashboard.autoService.serviceCatalog");
  const tCommon = useTranslations("Common");
  const { data: items, isLoading, isError } = useServiceCatalog();
  const deleteItem = useDeleteServiceItem();

  const [showCreate, setShowCreate] = useState(false);
  const [editingItem, setEditingItem] = useState<ServiceCatalogItemDto | null>(null);

  if (isLoading) {
    return <div style={{ padding: "32px", color: "#6B7280", fontSize: 13 }}>{tCommon("loading")}</div>;
  }
  if (isError) {
    return <div style={{ padding: "32px", color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>;
  }

  const columns: TableColumn<ServiceCatalogItemDto>[] = [
    {
      key: "name",
      header: t("headerName"),
      cellStyle: { color: "#E8EDF5", fontWeight: 600 },
      render: (item) => item.name,
    },
    {
      key: "description",
      header: t("headerDescription"),
      cellStyle: { color: "#6B7280" },
      render: (item) => item.description ?? "—",
    },
    {
      key: "price",
      header: t("headerPrice"),
      cellStyle: { color: "#E8EDF5" },
      render: (item) => item.defaultPrice.toFixed(2),
    },
    {
      key: "duration",
      header: t("headerDuration"),
      render: (item) => (item.durationMinutes ? `${item.durationMinutes} ${t("durationUnit")}` : "—"),
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (item) => (
        <StatusPill bg={item.isActive ? "#064E3B" : "#1F2937"} text={item.isActive ? "#34D399" : "#6B7280"}>
          {item.isActive ? t("statusActive") : t("statusInactive")}
        </StatusPill>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (item) => (
        <div style={{ display: "flex", gap: 6, justifyContent: "flex-end", alignItems: "center" }}>
          <DeactivateButton item={item} />
          <button
            onClick={() => setEditingItem(item)}
            style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: "4px 6px" }}
            title={t("editTitle")}
          >
            <Pencil size={13} />
          </button>
          <button
            onClick={() => {
              if (confirm(t("deleteConfirm", { name: item.name }))) {
                deleteItem.mutate(item.id);
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
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        actions={
          <Btn icon={<Plus size={16} />} onClick={() => setShowCreate(true)}>
            {t("addService")}
          </Btn>
        }
      />

      <Table
        columns={columns}
        rows={items ?? []}
        rowKey={(item) => item.id}
        emptyMessage={t("empty")}
      />

      {showCreate && <ServiceItemForm onClose={() => setShowCreate(false)} />}
      {editingItem && (
        <ServiceItemForm item={editingItem} onClose={() => setEditingItem(null)} />
      )}
    </>
  );
}
