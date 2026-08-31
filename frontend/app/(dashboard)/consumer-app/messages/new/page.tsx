"use client";

import { useTranslations } from "next-intl";
import { AccessDenied } from "@/components/AccessDenied";
import { useMe } from "@/features/auth/hooks/useAuth";
import { CustomerMessageForm } from "@/features/notifications/components/CustomerMessageForm";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";

export default function NewCustomerMessagePage() {
  const t = useTranslations("Dashboard.consumerApp.customerMessagesPage");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN) : null;
  if (roleAccess === false) return <AccessDenied title={t("newTitle")} />;
  if (roleAccess === null) return null;

  return <div style={{ padding: "28px 32px", width: "100%", boxSizing: "border-box" }}><div style={{ marginBottom: 22 }}><h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("newTitle")}</h1><p style={{ color: "#64748B", fontSize: 13, margin: "6px 0 0" }}>{t("newSubtitle")}</p></div><CustomerMessageForm /></div>;
}
