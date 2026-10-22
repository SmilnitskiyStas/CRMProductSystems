"use client";

import { useTranslations } from "next-intl";
import { AccessDenied } from "@/components/AccessDenied";
import { useMe } from "@/features/auth/hooks/useAuth";
import { CustomerMessageForm } from "@/features/notifications/components/CustomerMessageForm";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";

export default function NewCustomerMessagePage() {
  const t = useTranslations("Dashboard.consumerApp.customerMessagesPage");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN) : null;
  if (roleAccess === false) return <AccessDenied title={t("newTitle")} />;
  if (roleAccess === null) return null;

  return <PageContainer><PageHeader title={t("newTitle")} subtitle={t("newSubtitle")} /><CustomerMessageForm /></PageContainer>;
}
