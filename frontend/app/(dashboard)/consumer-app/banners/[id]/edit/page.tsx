"use client";

import { useParams, useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { AccessDenied } from "@/components/AccessDenied";
import { useMe } from "@/features/auth/hooks/useAuth";
import { BannerForm } from "@/features/consumer-app/components/BannerForm";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";

export default function EditConsumerAppBannerPage() {
  const router = useRouter();
  const params = useParams<{ id: string }>();
  const t = useTranslations("Dashboard.consumerApp.bannersPage");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN) : null;

  if (roleAccess === false) return <AccessDenied title={t("title")} />;
  if (roleAccess === null) return null;

  return (
    <div style={{ padding: "28px 32px", width: "100%", boxSizing: "border-box" }}>
      <BannerForm
        bannerId={params.id}
        presentation="page"
        onClose={() => router.push("/consumer-app/banners")}
      />
    </div>
  );
}
