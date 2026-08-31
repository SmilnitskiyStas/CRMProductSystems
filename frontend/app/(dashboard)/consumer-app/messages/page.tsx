"use client";

import { Bell, MessageCircle, Plus, Send } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { AccessDenied } from "@/components/AccessDenied";
import { useMe } from "@/features/auth/hooks/useAuth";
import { CustomerMessageHistory } from "@/features/notifications/components/CustomerMessageHistory";
import { AT_LEAST_ENTERPRISE_ADMIN, hasRole } from "@/lib/roles";
import { Btn } from "@/components/ui/Btn";

export default function ConsumerAppMessagesPage() {
  const t = useTranslations("Dashboard.consumerApp.customerMessagesPage");
  const router = useRouter();
  const { data: me } = useMe();
  const roleAccess = me ? hasRole(me.role, AT_LEAST_ENTERPRISE_ADMIN) : null;

  if (roleAccess === false) return <AccessDenied title={t("title")} />;
  if (roleAccess === null) return null;

  return (
    <div style={{ padding: "28px 32px", width: "100%", boxSizing: "border-box", display: "flex", flexDirection: "column", gap: 20 }}>
      <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
      <p style={{ color: "#64748B", fontSize: 14, margin: "-14px 0 0" }}>{t("subtitle")}</p>

      <section style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 14, padding: 24 }}>
        <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", gap: 20, flexWrap: "wrap" }}>
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 17, margin: 0 }}>{t("cardTitle")}</h2>
            <p style={{ color: "#64748B", fontSize: 13, margin: "7px 0 16px" }}>{t("cardDescription")}</p>
            <div style={{ display: "flex", gap: 9, flexWrap: "wrap" }}>
              {[[Bell, "Push"], [MessageCircle, "Messenger"], [Send, "SMS"]].map(([Icon, label]) => {
                const ChannelIcon = Icon as typeof Bell;
                return <span key={label as string} style={{ display: "flex", alignItems: "center", gap: 6, color: "#93C5FD", background: "#172554", border: "1px solid #1E3A8A", borderRadius: 8, padding: "7px 10px", fontSize: 12 }}><ChannelIcon size={14}/>{label as string}</span>;
              })}
            </div>
          </div>
          <Btn icon={<Plus size={16}/>} onClick={() => router.push("/consumer-app/messages/new")}>{t("createButton")}</Btn>
        </div>
        <p style={{ color: "#64748B", background: "#111827", borderRadius: 8, padding: "10px 12px", fontSize: 12, margin: "22px 0 0" }}>{t("integrationHint")}</p>
      </section>

      <CustomerMessageHistory />

    </div>
  );
}
