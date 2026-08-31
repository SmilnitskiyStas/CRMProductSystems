"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useRouter } from "next/navigation";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useCustomerMessageCampaigns, useSubmitCustomerMessage } from "../hooks/useNotifications";
import { CHANNEL_ICONS, getChannelLabel, type CustomerMessageCampaignItem } from "../types";

type AudienceDefinition = { segment?: string; estimatedRecipients?: number };

function parseDefinition(value: string): AudienceDefinition {
  if (!value) return {};
  try {
    const parsed = JSON.parse(value) as AudienceDefinition & { Segment?: string; EstimatedRecipients?: number };
    return { segment: parsed.segment ?? parsed.Segment, estimatedRecipients: parsed.estimatedRecipients ?? parsed.EstimatedRecipients };
  } catch { return {}; }
}

export function CustomerMessageHistory() {
  const t = useTranslations("Dashboard.consumerApp.customerMessagesPage.history");
  const tChannels = useTranslations("Dashboard.notifications.channels");
  const locale = useLocale();
  const router = useRouter();
  const [page, setPage] = useState(1);
  const pageSize = 20;
  const { data, isLoading, isError } = useCustomerMessageCampaigns(page, pageSize);
  const submitCampaign = useSubmitCustomerMessage();

  const columns: TableColumn<CustomerMessageCampaignItem>[] = [
    {
      key: "message",
      header: t("message"),
      render: (row) => {
        return <div style={{ minWidth: 220 }}><button type="button" onClick={() => router.push(`/consumer-app/messages/${row.id}`)} style={{ border: 0, padding: 0, background: "transparent", color: "#E8EDF5", fontSize: 13, fontWeight: 700, cursor: "pointer", textAlign: "left" }}>{row.title}</button><p style={{ color: "#6B7280", fontSize: 12, margin: "4px 0 0", maxWidth: 440, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{row.message}</p>{row.contentTitle && <span style={{ display: "inline-flex", marginTop: 6, padding: "3px 7px", borderRadius: 999, background: "#172554", color: "#93C5FD", fontSize: 11 }}>{row.contentType === "promotion" ? "Акція" : row.contentType === "banner" ? "Банер" : "Каталог"}: {row.contentTitle}</span>}</div>;
      },
    },
    {
      key: "audience",
      header: t("audience"),
      render: (row) => {
        if (row.audienceSource === "all_customers") return t("allCustomers");
        if (row.audienceSource === "rfm_segment") return `${parseDefinition(row.audienceDefinition).segment ?? "RFM"} (${row.resolvedRecipients})`;
        if (row.audienceSource === "purchase_history") return `За покупками (${row.resolvedRecipients})`;
        return t("loyaltyMembers");
      },
    },
    {
      key: "channel",
      header: t("channel"),
      render: (row) => {
        return <span style={{ display: "inline-flex", gap: 8, flexWrap: "wrap", justifyContent: "center" }}>{row.channels.map((channel) => <span key={channel}>{CHANNEL_ICONS[channel]} {channel === "messenger" && row.messengerProvider ? row.messengerProvider : getChannelLabel(tChannels, channel)}</span>)}</span>;
      },
    },
    {
      key: "status",
      header: t("status"),
      render: (row) => <div style={{ display: "flex", alignItems: "center", gap: 7, justifyContent: "center" }}><span style={{ display: "inline-flex", padding: "4px 8px", borderRadius: 999, background: row.status === "draft" ? "#27272A" : row.status === "scheduled" ? "#422006" : "#172554", color: row.status === "draft" ? "#D4D4D8" : row.status === "scheduled" ? "#FCD34D" : "#93C5FD", fontSize: 11 }}>{row.status === "draft" ? "Чернетка" : row.status === "scheduled" ? "Заплановано" : t("integrationPending")}</span>{row.status === "draft" && <button type="button" disabled={submitCampaign.isPending} onClick={() => submitCampaign.mutate({ id: row.id, deliveryMode: "send_now" })} style={{ border: "1px solid #2563EB", background: "transparent", color: "#93C5FD", borderRadius: 6, padding: "3px 7px", cursor: "pointer", fontSize: 11 }}>Відправити</button>}</div>,
    },
    {
      key: "createdAt",
      header: t("createdAt"),
      render: (row) => <div>{new Intl.DateTimeFormat(locale === "uk" ? "uk-UA" : "en-US", { dateStyle: "medium", timeStyle: "short" }).format(new Date(row.createdAt))}{row.scheduledAt && <div style={{ color: "#FCD34D", fontSize: 11, marginTop: 3 }}>→ {new Intl.DateTimeFormat(locale === "uk" ? "uk-UA" : "en-US", { dateStyle: "short", timeStyle: "short" }).format(new Date(row.scheduledAt))}</div>}</div>,
    },
  ];

  return (
    <section style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 12, padding: 24 }}>
      <div style={{ marginBottom: 18 }}><h2 style={{ color: "#E8EDF5", fontSize: 15, margin: 0 }}>{t("title")}</h2><p style={{ color: "#6B7280", fontSize: 12, margin: "4px 0 0" }}>{t("subtitle")}</p></div>
      {isError ? <p style={{ color: "#F87171", fontSize: 13 }}>{t("loadError")}</p> : <Table columns={columns} rows={data?.items ?? []} rowKey={(row) => row.id} page={page} totalPages={data?.totalPages ?? Math.max(1, Math.ceil((data?.totalCount ?? 0) / pageSize))} totalCount={data?.totalCount ?? 0} onPageChange={setPage} isLoading={isLoading} emptyMessage={t("empty")} minWidth={850} />}
    </section>
  );
}
