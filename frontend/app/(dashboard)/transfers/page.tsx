"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeftRight, Eye, CheckCircle, XCircle, BarChart2 } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import {
  useTransfers,
  useConfirmTransfer,
  useCancelTransfer,
} from "@/features/transfers/hooks/useTransfers";
import type { TransferDto, TransferStatus } from "@/features/transfers/types";
import { TRANSFER_STATUS_COLOR } from "@/features/transfers/types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_RECEIVE_STOCK, hasRole } from "@/lib/roles";
import { ActionMenu } from "@/components/ui/ActionMenu";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";

const STATUS_TAB_VALUES = ["", "draft", "in_transit", "received"] as const;

function StatusBadge({ status }: { status: TransferStatus }) {
  const t = useTranslations("Dashboard.transfers.status");
  const c = TRANSFER_STATUS_COLOR[status] ?? TRANSFER_STATUS_COLOR.draft;
  return (
    <span
      style={{
        display: "inline-block",
        padding: "3px 8px",
        borderRadius: 20,
        background: c.bg,
        color: c.text,
        fontSize: 11,
        fontWeight: 600,
      }}
    >
      {t.has(status) ? t(status) : status}
    </span>
  );
}

const tdStyle: React.CSSProperties = {
  padding: "10px 16px",
  color: "#9CA3AF",
  fontSize: 13,
  borderBottom: "1px solid #1F2937",
  borderRight: "1px solid #1F2937",
  textAlign: "center",
};

const thStyle: React.CSSProperties = {
  padding: "10px 16px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  borderBottom: "1px solid #374151",
  borderRight: "1px solid #374151",
  background: "#0A0F1A",
  textAlign: "center",
};

// ── Detail drawer content ────────────────────────────────────────────────────
function TransferDetail({ t: transfer, onViewAnalytics }: { t: TransferDto; onViewAnalytics: (productId: string) => void }) {
  const t = useTranslations("Dashboard.transfers.drawer");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  return (
    <>
      <DrawerSection title={t("section")}>
        <DrawerGrid>
          <DrawerField label={t("from")} value={transfer.fromStoreName} />
          <DrawerField label={t("to")} value={transfer.toStoreName} />
          <DrawerField
            label={t("status")}
            value={<StatusBadge status={transfer.status as TransferStatus} />}
          />
          <DrawerField
            label={t("transferType")}
            value={transfer.transferType ?? "—"}
          />
          <DrawerField
            label={t("createdAt")}
            value={new Date(transfer.createdAt).toLocaleDateString(intlLocale)}
          />
          <DrawerField label={t("items")} value={transfer.items.length} />
        </DrawerGrid>
        {transfer.notes && <DrawerField label={t("notes")} value={transfer.notes} />}
        <DrawerField
          label={t("documentId")}
          value={
            <span style={{ fontFamily: "monospace", fontSize: 12, color: "#4B5563" }}>
              {transfer.id}
            </span>
          }
        />
      </DrawerSection>

      <DrawerSection title={t("itemsSection", { count: transfer.items.length })}>
        {transfer.items.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noItems")}</div>
        ) : (
          <div style={{ border: "1px solid #1F2937", borderRadius: 8, overflow: "hidden" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr>
                  {[
                    t("itemHeaders.product"),
                    t("itemHeaders.batch"),
                    t("itemHeaders.qty"),
                    t("itemHeaders.expiry"),
                    t("itemHeaders.actions"),
                  ].map((h) => (
                    <th
                      key={h}
                      style={{
                        padding: "7px 10px",
                        color: "#4B5563",
                        fontWeight: 600,
                        textTransform: "uppercase",
                        fontSize: 10,
                        letterSpacing: "0.05em",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        background: "#0A0F1A",
                        textAlign: "center",
                      }}
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {transfer.items.map((item) => (
                  <tr key={item.id}>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#E8EDF5",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        fontWeight: 500,
                      }}
                    >
                      {item.productName}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#4B5563",
                        fontFamily: "monospace",
                        fontSize: 11,
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.batchNumber ?? "—"}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#9CA3AF",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.quantity}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#9CA3AF",
                        borderBottom: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.expiryDate
                        ? new Date(item.expiryDate).toLocaleDateString(intlLocale)
                        : "—"}
                    </td>
                    <td style={{ padding: "7px 10px", borderBottom: "1px solid #1F2937", textAlign: "center" }}>
                      <ActionMenu
                        items={[
                          {
                            label: t("analyticsAction"),
                            icon: <BarChart2 size={13} />,
                            onClick: () => onViewAnalytics(item.productId),
                          },
                        ]}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </DrawerSection>
    </>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────
export default function TransfersPage() {
  const t = useTranslations("Dashboard.transfers");
  const tPage = useTranslations("Dashboard.transfers.page");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_RECEIVE_STOCK) : null;

  const [statusFilter, setStatusFilter] = useState("");
  const { data: transfers = [], isLoading } = useTransfers(
    statusFilter ? { status: statusFilter } : undefined,
    access === true,
  );
  const confirm = useConfirmTransfer();
  const cancel = useCancelTransfer();

  const [selected, setSelected] = useState<TransferDto | null>(null);

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  const statusTabLabel = (value: string) =>
    value === "" ? tPage("statusTabs.all") : t(`status.${value}`);

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div>
        <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
          {tPage("subtitle")}
        </p>
      </div>

      {/* Status tabs */}
      <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937" }}>
        {STATUS_TAB_VALUES.map((value) => {
          const active = value === statusFilter;
          return (
            <button
              key={value}
              onClick={() => setStatusFilter(value)}
              style={{
                background: "transparent",
                border: "none",
                borderBottom: active ? "2px solid #3B82F6" : "2px solid transparent",
                color: active ? "#60A5FA" : "#6B7280",
                fontSize: 13,
                fontWeight: active ? 600 : 400,
                padding: "8px 14px",
                cursor: "pointer",
                marginBottom: -1,
                transition: "color 0.1s",
              }}
            >
              {statusTabLabel(value)}
            </button>
          );
        })}
      </div>

      {/* Table */}
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 12,
          overflow: "hidden",
        }}
      >
        {isLoading ? (
          <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
            {tCommon("loading")}
          </div>
        ) : transfers.length === 0 ? (
          <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
            {tPage("empty")}
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                {[
                  tPage("headers.from"),
                  "",
                  tPage("headers.to"),
                  tPage("headers.items"),
                  tPage("headers.date"),
                  tPage("headers.status"),
                  tPage("headers.actions"),
                ].map((h, i) => (
                  <th key={i === 1 ? "arrow" : h} style={thStyle}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {transfers.map((tr) => (
                <tr key={tr.id}>
                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500 }}>
                    {tr.fromStoreName}
                  </td>
                  <td style={{ ...tdStyle, color: "#4B5563" }}>
                    <ArrowLeftRight size={14} />
                  </td>
                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500 }}>
                    {tr.toStoreName}
                  </td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>{tr.items.length}</td>
                  <td style={tdStyle}>
                    {new Date(tr.createdAt).toLocaleDateString(intlLocale)}
                  </td>
                  <td style={tdStyle}>
                    <StatusBadge status={tr.status as TransferStatus} />
                  </td>
                  <td style={{ ...tdStyle, borderRight: "none" }}>
                    <ActionMenu
                      items={[
                        {
                          label: tPage("actionMenu.view"),
                          icon: <Eye size={13} />,
                          onClick: () => setSelected(tr),
                        },
                        { separator: true },
                        ...(tr.status === "in_transit"
                          ? [
                              {
                                label: tPage("actionMenu.confirm"),
                                icon: <CheckCircle size={13} />,
                                variant: "success" as const,
                                disabled: confirm.isPending,
                                onClick: () => confirm.mutate(tr.id),
                              },
                            ]
                          : []),
                        ...(tr.status === "draft" || tr.status === "in_transit"
                          ? [
                              {
                                label: tCommon("cancel"),
                                icon: <XCircle size={13} />,
                                variant: "danger" as const,
                                disabled: cancel.isPending,
                                onClick: () => cancel.mutate(tr.id),
                              },
                            ]
                          : []),
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Detail drawer */}
      <DetailDrawer
        isOpen={selected !== null}
        onClose={() => setSelected(null)}
        title={t("title")}
        subtitle={
          selected
            ? `${selected.fromStoreName} → ${selected.toStoreName} · ${new Date(selected.createdAt).toLocaleDateString(intlLocale)}`
            : ""
        }
      >
        {selected && (
          <TransferDetail
            t={selected}
            onViewAnalytics={(productId) => router.push(`/inventory/${productId}?tab=analytics`)}
          />
        )}
      </DetailDrawer>
    </div>
  );
}
