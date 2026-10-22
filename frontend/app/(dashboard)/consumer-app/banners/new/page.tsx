"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { AccessDenied } from "@/components/AccessDenied";
import { useMe } from "@/features/auth/hooks/useAuth";
import { BannerForm } from "@/features/consumer-app/components/BannerForm";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { PageContainer } from "@/components/ui/PageContainer";

export default function CreateConsumerAppBannerPage() {
  const router = useRouter();
  const t = useTranslations("Dashboard.consumerApp.bannersPage");
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN) : null;

  if (roleAccess === false) return <AccessDenied title={t("title")} />;
  if (roleAccess === null) return null;

  return (
    <PageContainer>
      <BannerForm
        bannerId={null}
        presentation="page"
        onClose={() => router.push("/consumer-app/banners")}
      />
    </PageContainer>
  );
}
