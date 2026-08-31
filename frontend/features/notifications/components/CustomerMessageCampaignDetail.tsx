"use client";

import { AlertTriangle, ArrowLeft, CheckCircle2, Clock3, Link2Off, Send } from "lucide-react";
import { useLocale } from "next-intl";
import { useRouter } from "next/navigation";
import { Btn } from "@/components/ui/Btn";
import { useCustomerMessageCampaign, useSubmitCustomerMessage } from "../hooks/useNotifications";
import { CHANNEL_ICONS } from "../types";

const card = { background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: 20 };

export function CustomerMessageCampaignDetail({ id }: { id: string }) {
  const router = useRouter();
  const locale = useLocale();
  const query = useCustomerMessageCampaign(id);
  const submit = useSubmitCustomerMessage();
  if (query.isLoading) return <p style={{ color: "#94A3B8" }}>Завантаження кампанії…</p>;
  if (query.isError || !query.data) return <p style={{ color: "#F87171" }}>Не вдалося завантажити кампанію.</p>;
  const { campaign, channels } = query.data;
  const date = (value: string | null) => value ? new Intl.DateTimeFormat(locale === "uk" ? "uk-UA" : "en-US", { dateStyle: "medium", timeStyle: "short" }).format(new Date(value)) : "—";

  return <div style={{ display: "flex", flexDirection: "column", gap: 18 }}>
    <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
      <Btn variant="ghost" icon={<ArrowLeft size={15}/>} onClick={() => router.push("/consumer-app/messages")}>До історії</Btn>
      {campaign.status === "draft" && <Btn icon={<Send size={15}/>} disabled={submit.isPending} onClick={() => submit.mutate({ id, deliveryMode: "send_now" }, { onSuccess: () => query.refetch() })}>Підготувати відправлення</Btn>}
    </div>
    <section style={card}>
      <div style={{ display: "flex", justifyContent: "space-between", gap: 20, flexWrap: "wrap" }}><div><h1 style={{ color: "#F1F5F9", fontSize: 22, margin: 0 }}>{campaign.title}</h1><p style={{ color: "#94A3B8", whiteSpace: "pre-wrap", lineHeight: 1.6 }}>{campaign.message}</p></div><span style={{ color: "#93C5FD", background: "#172554", borderRadius: 999, padding: "6px 10px", height: "fit-content", fontSize: 12 }}>{campaign.status}</span></div>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: 12, color: "#94A3B8", fontSize: 12 }}><div>Створено<br/><strong style={{ color: "#E2E8F0" }}>{date(campaign.createdAt)}</strong></div><div>Заплановано<br/><strong style={{ color: "#E2E8F0" }}>{date(campaign.scheduledAt)}</strong></div><div>Одержувачів<br/><strong style={{ color: "#E2E8F0" }}>{campaign.resolvedRecipients}</strong></div><div>Аудиторія<br/><strong style={{ color: "#E2E8F0" }}>{campaign.audienceSource}</strong></div></div>
    </section>
    {!query.data.providersConnected && <div style={{ display: "flex", gap: 10, alignItems: "center", color: "#FCD34D", background: "#422006", border: "1px solid #854D0E", borderRadius: 10, padding: 13, fontSize: 13 }}><Link2Off size={17}/> Провайдери доставки ще не підключені. Статистика показує підготовлений обсяг, фактичних відправлень немає.</div>}
    <div style={{ display: "grid", gridTemplateColumns: "repeat(4, minmax(140px, 1fr))", gap: 12 }}>
      {[["Всього доставок", query.data.totalDeliveries, Send, "#93C5FD"], ["Надіслано", query.data.sentCount, CheckCircle2, "#4ADE80"], ["Очікує", query.data.pendingCount, Clock3, "#FCD34D"], ["Помилки", query.data.failedCount, AlertTriangle, "#F87171"]].map(([label, value, Icon, color]) => { const I = Icon as typeof Send; return <div key={label as string} style={card}><I size={18} color={color as string}/><div style={{ color: "#64748B", fontSize: 11, marginTop: 10 }}>{label as string}</div><strong style={{ color: color as string, fontSize: 22 }}>{value as number}</strong></div>; })}
    </div>
    <section style={card}><h2 style={{ color: "#E2E8F0", fontSize: 15, margin: "0 0 14px" }}>Канали</h2><div style={{ display: "grid", gap: 9 }}>{channels.map((item) => <div key={item.channel} style={{ display: "grid", gridTemplateColumns: "1fr repeat(4, minmax(70px, auto))", gap: 12, alignItems: "center", borderTop: "1px solid #1F2937", paddingTop: 10, color: "#94A3B8", fontSize: 12 }}><strong style={{ color: "#E2E8F0" }}>{CHANNEL_ICONS[item.channel]} {item.channel}</strong><span>{item.recipientCount} отримувачів</span><span style={{ color: "#4ADE80" }}>{item.sentCount} надіслано</span><span style={{ color: "#FCD34D" }}>{item.pendingCount} очікує</span><span style={{ color: "#F87171" }}>{item.failedCount} помилок</span></div>)}</div></section>
  </div>;
}
