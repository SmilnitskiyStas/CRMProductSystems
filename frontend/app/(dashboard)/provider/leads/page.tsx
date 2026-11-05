"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { Inbox } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { LeadsPanel } from "@/features/provider/components/LeadsPanel";

const PROVIDER_ROLES = ["provider", "provider_admin", "provider_agent"];

export default function ProviderLeadsPage() {
  const t = useTranslations("Dashboard.providerLeads");
  const router = useRouter();
  const { data: me, isLoading: meLoading } = useMe();

  useEffect(() => {
    if (!meLoading && me && !PROVIDER_ROLES.includes(me.role)) {
      router.replace("/dashboard");
    }
  }, [me, meLoading, router]);

  if (meLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#4B5563", fontSize: 14 }}>{t("loading")}</div>
      </div>
    );
  }

  if (!me || !PROVIDER_ROLES.includes(me.role)) return null;

  return (
    <PageContainer>
      <PageHeader
        title={
          <span style={{ display: "inline-flex", alignItems: "center", gap: 10 }}>
            <span
              style={{
                width: 34,
                height: 34,
                borderRadius: 9,
                background: "linear-gradient(135deg, #7C3AED, #3B82F6)",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                flexShrink: 0,
              }}
            >
              <Inbox size={17} color="#fff" />
            </span>
            {t("title")}
          </span>
        }
        subtitle={t("subtitle")}
      />
      <LeadsPanel />
    </PageContainer>
  );
}
