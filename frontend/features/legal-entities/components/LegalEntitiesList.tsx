"use client";

import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Table, type TableColumn } from "@/components/ui/Table";
import type { LegalEntityDto } from "../types";

interface Props {
  entities: LegalEntityDto[];
  isLoading: boolean;
  canManage: boolean;
  onEdit: (entity: LegalEntityDto) => void;
  onDeactivate: (entity: LegalEntityDto) => void;
}

export function LegalEntitiesList({ entities, isLoading, canManage, onEdit, onDeactivate }: Props) {
  const t = useTranslations("Dashboard.legalEntities.list");

  if (isLoading) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
        {t("loading")}
      </div>
    );
  }

  if (!entities.length) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, textAlign: "center", padding: "48px 0" }}>
        {t("emptyPrefix")} {canManage && t("emptyHint")}
      </div>
    );
  }

  const columns: TableColumn<LegalEntityDto>[] = [
    {
      key: "name",
      header: t("headerName"),
      render: (entity) => (
        <span style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 13 }}>{entity.legalName}</span>
      ),
    },
    {
      key: "edrpou",
      header: t("headerEdrpou"),
      cellStyle: { color: "#9CA3AF", fontSize: 13 },
      render: (entity) => entity.edrpou ?? "—",
    },
    {
      key: "director",
      header: t("headerDirector"),
      cellStyle: { color: "#9CA3AF", fontSize: 13 },
      render: (entity) => entity.directorName ?? "—",
    },
    {
      key: "vat",
      header: t("headerVat"),
      render: (entity) => (
        <span
          style={{
            background: entity.isVatPayer ? "#1D2D4A" : "transparent",
            color: entity.isVatPayer ? "#60A5FA" : "#4B5563",
            borderRadius: 6,
            padding: "2px 8px",
            fontSize: 11,
            fontWeight: 600,
          }}
        >
          {entity.isVatPayer ? t("vatYes") : t("vatNo")}
        </span>
      ),
    },
    {
      key: "status",
      header: t("headerStatus"),
      render: (entity) => (
        <span
          style={{
            color: entity.isActive ? "#22c55e" : "#6B7280",
            fontSize: 12,
            fontWeight: 600,
          }}
        >
          {entity.isActive ? t("statusActive") : t("statusInactive")}
        </span>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (entity) =>
        canManage && (
          <div style={{ display: "flex", alignItems: "center", gap: 8, justifyContent: "center" }}>
            <Btn variant="ghost" size="sm" onClick={() => onEdit(entity)}>
              {t("editButton")}
            </Btn>
            {entity.isActive && (
              <Btn variant="danger" size="sm" onClick={() => onDeactivate(entity)}>
                {t("deactivateButton")}
              </Btn>
            )}
          </div>
        ),
    },
  ];

  return <Table columns={columns} rows={entities} rowKey={(entity) => entity.id} />;
}
