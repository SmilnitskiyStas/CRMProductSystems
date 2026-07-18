"use client";

import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
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

  const headers = [t("headerName"), t("headerEdrpou"), t("headerDirector"), t("headerVat"), t("headerStatus"), ""];

  return (
    <div
      style={{
        background: "#161B26",
        border: "1px solid #1F2937",
        borderRadius: 12,
        overflow: "hidden",
      }}
    >
      <table style={{ width: "100%", borderCollapse: "collapse" }}>
        <thead>
          <tr style={{ borderBottom: "1px solid #1F2937" }}>
            {headers.map((h) => (
              <th
                key={h}
                style={{
                  color: "#4B5563",
                  fontSize: 11,
                  fontWeight: 600,
                  textTransform: "uppercase",
                  letterSpacing: "0.04em",
                  padding: "12px 16px",
                  textAlign: "left",
                }}
              >
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {entities.map((entity) => (
            <tr key={entity.id} style={{ borderBottom: "1px solid #1F2937" }}>
              <td style={tdStyle}>
                <span style={{ color: "#E8EDF5", fontWeight: 600, fontSize: 13 }}>{entity.legalName}</span>
              </td>
              <td style={{ ...tdStyle, color: "#9CA3AF", fontSize: 13 }}>{entity.edrpou ?? "—"}</td>
              <td style={{ ...tdStyle, color: "#9CA3AF", fontSize: 13 }}>{entity.directorName ?? "—"}</td>
              <td style={tdStyle}>
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
              </td>
              <td style={tdStyle}>
                <span
                  style={{
                    color: entity.isActive ? "#22c55e" : "#6B7280",
                    fontSize: 12,
                    fontWeight: 600,
                  }}
                >
                  {entity.isActive ? t("statusActive") : t("statusInactive")}
                </span>
              </td>
              <td style={{ ...tdStyle, textAlign: "right" }}>
                {canManage && (
                  <div style={{ display: "flex", alignItems: "center", gap: 8, justifyContent: "flex-end" }}>
                    <Btn variant="ghost" size="sm" onClick={() => onEdit(entity)}>
                      {t("editButton")}
                    </Btn>
                    {entity.isActive && (
                      <Btn variant="danger" size="sm" onClick={() => onDeactivate(entity)}>
                        {t("deactivateButton")}
                      </Btn>
                    )}
                  </div>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

const tdStyle: React.CSSProperties = {
  padding: "14px 16px",
  verticalAlign: "middle",
};
