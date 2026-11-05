"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { useTranslations } from "next-intl";
import { Users, BarChart2 } from "lucide-react";
import { Shield } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { PROVIDER_TEAM } from "@/lib/roles";
import type { AppRole } from "@/lib/roles";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { Tabs } from "@/components/ui/Tabs";
import { TeamTab } from "@/features/provider/components/TeamTab";
import { StatsTab } from "@/features/provider/components/StatsTab";
import { RolesSection } from "@/features/provider/components/RolesSection";

type Tab = "team" | "stats" | "roles";

export default function ProviderTeamPage() {
  const t = useTranslations("Dashboard.provider.teamPage");
  const router = useRouter();
  const { data: me, isLoading } = useMe();
  const [activeTab, setActiveTab] = useState<Tab>("team");

  useEffect(() => {
    if (!isLoading && me && !PROVIDER_TEAM.has((me.role ?? "") as AppRole)) {
      router.replace("/dashboard");
    }
  }, [me, isLoading, router]);

  if (isLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#4B5563", fontSize: 14 }}>{t("loading")}</div>
      </div>
    );
  }

  if (!me || !PROVIDER_TEAM.has((me.role ?? "") as AppRole)) return null;

  return (
    <PageContainer>
      <PageHeader
        title={
          <span style={{ display: "inline-flex", alignItems: "center", gap: 10 }}>
            <Users size={20} style={{ color: "#3B82F6" }} />
            {t("title")}
          </span>
        }
        subtitle={t("subtitle")}
      />

      <Tabs
        items={[
          { key: "team", label: <><Users size={14} /> {t("tabTeam")}</> },
          { key: "stats", label: <><BarChart2 size={14} /> {t("tabStats")}</> },
          { key: "roles", label: <><Shield size={14} /> {t("tabRoles")}</> },
        ]}
        value={activeTab}
        onChange={setActiveTab}
      />

      {activeTab === "team"  && <TeamTab />}
      {activeTab === "stats" && <StatsTab />}
      {activeTab === "roles" && <RolesSection standalone />}
    </PageContainer>
  );
}
