"use client";

import type { CSSProperties } from "react";
import { useTranslations } from "next-intl";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useModules } from "@/features/modules/hooks/useModules";
import { useUsers } from "@/features/users/hooks/useUsers";
import { LanguageSwitcher } from "@/features/profile/components/LanguageSwitcher";
import { getRoleLabel } from "@/features/profile/types";
import {
  hasRole,
  AT_LEAST_STORE_MANAGER,
  AT_LEAST_ENTERPRISE_ADMIN,
  canManageLegalEntities,
} from "@/lib/roles";

const sectionStyle: CSSProperties = {
  paddingBottom: 20,
  marginBottom: 20,
  borderBottom: "1px solid #1A2235",
};

const h3Style: CSSProperties = {
  color: "#E8EDF5",
  fontSize: 14,
  fontWeight: 600,
  margin: "0 0 4px",
};

const subStyle: CSSProperties = {
  color: "#4B5563",
  fontSize: 12,
  margin: "0 0 14px",
  lineHeight: 1.5,
};

const linkCardStyle: CSSProperties = {
  display: "block",
  padding: "12px 14px",
  background: "#0A1020",
  border: "1px solid #1F2937",
  borderRadius: 9,
  color: "#93C5FD",
  fontSize: 13,
  fontWeight: 500,
  textDecoration: "none",
};

export function OverviewTab() {
  const t = useTranslations("Dashboard.settings.overviewTab");
  const tRoles = useTranslations("Dashboard.roles");
  const tCatalog = useTranslations("Dashboard.modules.catalog");
  const tBusinessTypes = useTranslations("Dashboard.modules.businessTypes");
  const { data: me } = useMe();

  const isManager = hasRole(me?.role, AT_LEAST_STORE_MANAGER);
  const isAdmin = hasRole(me?.role, AT_LEAST_ENTERPRISE_ADMIN);
  const showLegalEntities = canManageLegalEntities(me?.role, me?.permissions);

  const { data: modules } = useModules(Boolean(me?.tenantId));
  const { data: users } = useUsers(isManager);

  if (!me) {
    return (
      <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>{t("loading")}</div>
    );
  }

  const businessTypeLabel =
    modules && tBusinessTypes.has(modules.businessType)
      ? tBusinessTypes(modules.businessType)
      : modules?.businessType ?? "";

  const activeUsers = users?.filter((u) => u.isActive).length ?? 0;
  const needsLocation = users?.filter((u) => u.needsLocationAssignment).length ?? 0;

  return (
    <div>
      {/* ── Context header ─────────────────────────────────────────────── */}
      <div
        style={{
          ...sectionStyle,
          display: "flex",
          flexWrap: "wrap",
          gap: "6px 28px",
        }}
      >
        <div style={{ fontSize: 13, color: "#6B7280" }}>
          {t("roleLabel")}:{" "}
          <strong style={{ color: "#E8EDF5", fontWeight: 600 }}>
            {getRoleLabel(tRoles, me.role)}
          </strong>
        </div>
        {me.tenantName && (
          <div style={{ fontSize: 13, color: "#6B7280" }}>
            {t("companyLabel")}:{" "}
            <strong style={{ color: "#E8EDF5", fontWeight: 600 }}>{me.tenantName}</strong>
          </div>
        )}
      </div>

      {/* ── Business modules (read-only) ───────────────────────────────── */}
      {modules && (
        <div style={sectionStyle}>
          <h3 style={h3Style}>{t("modulesTitle")}</h3>
          <p style={subStyle}>
            {t("businessTypeLabel")}:{" "}
            <strong style={{ color: "#9CA3AF" }}>{businessTypeLabel}</strong>
            {" — "}
            {t("modulesHint")}
          </p>
          {modules.modules.length === 0 ? (
            <div style={{ color: "#4B5563", fontSize: 12 }}>{t("modulesNone")}</div>
          ) : (
            <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
              {modules.modules.map((key) => (
                <span
                  key={key}
                  style={{
                    padding: "5px 11px",
                    borderRadius: 6,
                    background: "#16213A",
                    color: "#93C5FD",
                    fontSize: 12,
                    fontWeight: 500,
                  }}
                >
                  {tCatalog.has(`${key}.label`) ? tCatalog(`${key}.label`) : key}
                </span>
              ))}
            </div>
          )}
        </div>
      )}

      {/* ── Quick actions ─────────────────────────────────────────────── */}
      <div style={sectionStyle}>
        <h3 style={h3Style}>{t("quickActionsTitle")}</h3>
        <p style={subStyle}>{t("quickActionsHint")}</p>
        <div
          style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fill, minmax(180px, 1fr))",
            gap: 10,
          }}
        >
          <a href="/settings-user" style={linkCardStyle}>
            {t("actionProfile")} →
          </a>
          <a href="/settings-user#password" style={linkCardStyle}>
            {t("actionPassword")} →
          </a>
          <a href="/settings-user#security" style={linkCardStyle}>
            {t("action2fa")} →
          </a>
        </div>

        <div
          style={{
            marginTop: 16,
            padding: 16,
            background: "#0A1020",
            border: "1px solid #1F2937",
            borderRadius: 9,
          }}
        >
          <LanguageSwitcher />
        </div>
      </div>

      {/* ── Team summary (managers+) ──────────────────────────────────── */}
      {isManager && (
        <div style={{ ...sectionStyle, borderBottom: "none", marginBottom: 0, paddingBottom: 0 }}>
          <h3 style={h3Style}>{t("teamTitle")}</h3>
          <p style={subStyle}>{t("teamHint")}</p>

          <div style={{ display: "flex", gap: 12, marginBottom: 14 }}>
            <div
              style={{
                flex: "0 1 160px",
                background: "#0A1020",
                border: "1px solid #1F2937",
                borderRadius: 9,
                padding: "12px 16px",
              }}
            >
              <div style={{ color: "#60A5FA", fontSize: 20, fontWeight: 700 }}>{activeUsers}</div>
              <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>{t("teamActive")}</div>
            </div>
            {needsLocation > 0 && (
              <div
                style={{
                  flex: "0 1 160px",
                  background: "#0A1020",
                  border: "1px solid #3A2E12",
                  borderRadius: 9,
                  padding: "12px 16px",
                }}
              >
                <div style={{ color: "#FBBF24", fontSize: 20, fontWeight: 700 }}>{needsLocation}</div>
                <div style={{ color: "#4B5563", fontSize: 12, marginTop: 2 }}>
                  {t("teamNeedsLocation")}
                </div>
              </div>
            )}
          </div>

          <div style={{ display: "flex", flexWrap: "wrap", gap: 10 }}>
            <a href="/users" style={{ ...linkCardStyle, flex: "0 1 auto" }}>
              {t("manageTeam")} →
            </a>
            {isAdmin && (
              <a href="/users?tab=role-templates" style={{ ...linkCardStyle, flex: "0 1 auto" }}>
                {t("manageRoles")} →
              </a>
            )}
            {showLegalEntities && (
              <a href="/settings/legal-entities" style={{ ...linkCardStyle, flex: "0 1 auto" }}>
                {t("legalEntities")} →
              </a>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
