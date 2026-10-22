"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useLocale, useTranslations } from "next-intl";
import { toast } from "sonner";
import { Archive, Pause, Pencil, Play, Plus, Rocket } from "lucide-react";
import { Table, type TableColumn } from "@/components/ui/Table";
import { ActionMenu, type ActionMenuItem } from "@/components/ui/ActionMenu";
import { Btn } from "@/components/ui/Btn";
import { PageHeader } from "@/components/ui/PageHeader";
import { StatusPill } from "@/components/ui/StatusPill";
import { Tabs } from "@/components/ui/Tabs";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { usePromotionRules, useRuleAction, type RuleAction } from "../hooks/usePromotionRules";
import { storesSummary } from "../lib/messages";
import type { RuleListItem, RuleStatus, ValidationResult } from "../types";
import { ApiError } from "@/lib/api";

const TABS: RuleStatus[] = ["draft", "published", "paused", "archived"];

const STATUS_COLORS: Record<RuleStatus, { bg: string; color: string }> = {
  draft: { bg: "#1a2233", color: "#9CA3AF" },
  published: { bg: "#1a3a2e", color: "#4ADE80" },
  paused: { bg: "#2a1f08", color: "#FBBF24" },
  archived: { bg: "#111827", color: "#6B7280" },
};

function formatPeriod(r: RuleListItem, locale: string, t: ReturnType<typeof useTranslations>): string {
  const fmt = (iso: string) => new Date(iso).toLocaleDateString(locale);
  if (!r.validFrom && !r.validTo) return t("period.always");
  if (r.validFrom && r.validTo) return `${fmt(r.validFrom)} - ${fmt(r.validTo)}`;
  if (r.validFrom) return t("period.from", { date: fmt(r.validFrom) });
  return t("period.to", { date: fmt(r.validTo!) });
}

export function RulesList() {
  const t = useTranslations("Dashboard.promotionRules");
  const locale = useLocale();
  const router = useRouter();
  // Global header store selector; empty = all stores the user may see (backend scopes by role).
  const storeId = usePrimaryStoreId();
  const [tab, setTab] = useState<RuleStatus>("draft");
  const { data: rules = [], isLoading } = usePromotionRules(tab, storeId);
  const actionMut = useRuleAction();

  function run(r: RuleListItem, action: RuleAction) {
    actionMut.mutate(
      { id: r.id, action },
      {
        onSuccess: () => toast.success(t(`toast.${action}`)),
        onError: (e) => {
          const body = e instanceof ApiError ? (e.body as ValidationResult | undefined) : undefined;
          if (body && Array.isArray(body.errors)) {
            toast.error(t("toast.publishBlocked"));
            router.push(`/promotion-rules/${r.id}`);
          } else toast.error(e.message);
        },
      },
    );
  }

  const columns: TableColumn<RuleListItem>[] = [
    {
      key: "name",
      header: t("columns.name"),
      render: (r) => (
        <div>
          <div style={{ color: "#E8EDF5", fontWeight: 600 }}>{r.name}</div>
          {r.description && <div style={{ color: "#6B7280", fontSize: 12 }}>{r.description}</div>}
        </div>
      ),
    },
    { key: "type", header: t("columns.type"), render: (r) => t(`types.${r.type}.title`) },
    { key: "stores", header: t("columns.stores"), render: (r) => storesSummary(r.storeIds, t) },
    { key: "period", header: t("columns.period"), render: (r) => formatPeriod(r, locale, t) },
    {
      key: "status",
      header: t("columns.status"),
      render: (r) => (
        <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
          <StatusPill bg={STATUS_COLORS[r.status].bg} text={STATUS_COLORS[r.status].color}>{t(`status.${r.status}`)}</StatusPill>
          {r.hasUnpublishedChanges && (
            <StatusPill bg="#1D3461" text="#93C5FD">
              {t("unpublished")}
            </StatusPill>
          )}
        </div>
      ),
    },
    {
      key: "actions",
      header: "",
      render: (r) => {
        const items: ActionMenuItem[] = [
          { label: t("actions.edit"), icon: <Pencil size={13} />, onClick: () => router.push(`/promotion-rules/${r.id}`) },
        ];
        if (r.status === "draft" || r.hasUnpublishedChanges) {
          items.push({
            label: t("actions.publish"),
            icon: <Rocket size={13} />,
            variant: "success",
            disabled: actionMut.isPending,
            onClick: () => run(r, "publish"),
          });
        }
        if (r.status === "published") {
          items.push({ label: t("actions.pause"), icon: <Pause size={13} />, variant: "warning", onClick: () => run(r, "pause") });
        }
        if (r.status === "paused") {
          items.push({ label: t("actions.resume"), icon: <Play size={13} />, variant: "success", onClick: () => run(r, "resume") });
        }
        if (r.status !== "archived") {
          items.push({ separator: true }, { label: t("actions.archive"), icon: <Archive size={13} />, variant: "danger", onClick: () => run(r, "archive") });
        }
        return (
          <div style={{ display: "flex", justifyContent: "center" }}>
            <ActionMenu label={t("actions.menu")} items={items} />
          </div>
        );
      },
    },
  ];

  return (
    <>
      <PageHeader
        title={t("title")}
        subtitle={t("subtitle")}
        actions={
          <Btn icon={<Plus size={15} />} onClick={() => router.push("/promotion-rules/new")}>
            {t("create")}
          </Btn>
        }
      />

      <Tabs items={TABS.map((k) => ({ key: k, label: t(`tabs.${k}`) }))} value={tab} onChange={setTab} />

      <Table
        columns={columns}
        rows={rules}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        minWidth={900}
        emptyMessage={t(`empty.${tab}`)}
      />
    </>
  );
}
