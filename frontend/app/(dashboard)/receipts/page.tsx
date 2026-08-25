"use client";

import { useEffect, useState } from "react";
import { Eye, ExternalLink, Plus } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { useReceipts } from "@/features/receipts/hooks/useReceipts";
import { ReceiptStatusBadge } from "@/features/receipts/components/ReceiptStatusBadge";
import { CreateReceiptForm } from "@/features/receipts/components/CreateReceiptForm";
import type { ReceiptDto, ReceiptStatus } from "@/features/receipts/types";
import { useMe } from "@/features/auth/hooks/useAuth";
import { usePrimaryStoreId } from "@/lib/useStoreContext";
import { AccessDenied } from "@/components/AccessDenied";
import { CAN_RECEIVE_STOCK, hasRole } from "@/lib/roles";
import { ActionMenu } from "@/components/ui/ActionMenu";
import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";
import { Pagination } from "@/components/ui/Pagination";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";

const STATUS_TAB_VALUES = ["", "draft", "in_transit", "received", "cancelled"] as const;

function formatDate(s: string | null, intlLocale: string) {
  if (!s) return "—";
  return new Date(s).toLocaleDateString(intlLocale);
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
function ReceiptDetail({ r }: { r: ReceiptDto }) {
  const t = useTranslations("Dashboard.receipts.drawer");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  return (
    <>
      <DrawerSection title={t("section")}>
        <DrawerGrid>
          <DrawerField label={t("supplier")} value={r.supplierName ?? "—"} />
          <DrawerField label={t("destinationStore")} value={r.destinationStoreName} />
          <DrawerField
            label={t("status")}
            value={<ReceiptStatusBadge status={r.status as ReceiptStatus} />}
          />
          <DrawerField label={t("expected")} value={formatDate(r.expectedAt, intlLocale)} />
          <DrawerField label={t("received")} value={formatDate(r.receivedAt, intlLocale)} />
          <DrawerField label={t("items")} value={r.items.length} />
          <DrawerField label={t("viaCentral")} value={r.viaCentralStore ? t("yes") : t("no")} />
          <DrawerField label={t("createdAt")} value={formatDate(r.createdAt, intlLocale)} />
        </DrawerGrid>
        {r.notes && <DrawerField label={t("notes")} value={r.notes} />}
        <DrawerField
          label={t("documentId")}
          value={
            <span style={{ fontFamily: "monospace", fontSize: 12, color: "#4B5563" }}>
              {r.id}
            </span>
          }
        />
      </DrawerSection>

      <DrawerSection title={t("itemsSection", { count: r.items.length })}>
        {r.items.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 13 }}>{t("noItems")}</div>
        ) : (
          <div style={{ border: "1px solid #1F2937", borderRadius: 8, overflow: "hidden" }}>
            <table style={{ width: "100%", borderCollapse: "collapse", fontSize: 12 }}>
              <thead>
                <tr>
                  {[
                    t("itemHeaders.product"),
                    t("itemHeaders.batch"),
                    t("itemHeaders.ordered"),
                    t("itemHeaders.received"),
                    t("itemHeaders.expiry"),
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
                {r.items.map((item) => (
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
                      {item.quantityOrdered}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        fontFamily: "monospace",
                        borderBottom: "1px solid #1F2937",
                        borderRight: "1px solid #1F2937",
                        textAlign: "center",
                        color:
                          item.quantityReceived != null &&
                          item.quantityReceived < item.quantityOrdered
                            ? "#FBBF24"
                            : "#4ADE80",
                      }}
                    >
                      {item.quantityReceived ?? "—"}
                    </td>
                    <td
                      style={{
                        padding: "7px 10px",
                        color: "#9CA3AF",
                        borderBottom: "1px solid #1F2937",
                        textAlign: "center",
                      }}
                    >
                      {item.expiryDate ? formatDate(item.expiryDate, intlLocale) : "—"}
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
export default function ReceiptsPage() {
  const { data: me } = useMe();
  const access = me ? hasRole(me.role, CAN_RECEIVE_STOCK) : null;

  const t = useTranslations("Dashboard.receipts");
  const tPage = useTranslations("Dashboard.receipts.page");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  const [statusFilter, setStatusFilter] = useState("");
  const primaryStoreId = usePrimaryStoreId();
  const [page, setPage] = useState(1);
  const PAGE_SIZE = 50;
  const { data, isLoading } = useReceipts(
    { store_id: primaryStoreId, status: statusFilter || undefined, page, pageSize: PAGE_SIZE },
    access === true,
  );
  const receipts = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? Math.ceil(totalCount / PAGE_SIZE);

  // Reset to page 1 whenever a filter changes underneath the current page.
  useEffect(() => {
    setPage(1);
  }, [statusFilter, primaryStoreId]);

  const [selected, setSelected] = useState<ReceiptDto | null>(null);
  const [showCreateModal, setShowCreateModal] = useState(false);

  if (access === null) return null;
  if (!access) return <AccessDenied title={t("title")} />;

  const statusTabLabel = (value: string) =>
    value === "" ? tPage("statusTabs.all") : t(`status.${value}`);

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {tPage("subtitle")}
          </p>
        </div>
        <Btn icon={<Plus size={15} />} onClick={() => setShowCreateModal(true)}>
          {tPage("newButton")}
        </Btn>
      </div>

      {/* Status tabs */}
      <div style={{ display: "flex", gap: 4, borderBottom: "1px solid #1F2937", paddingBottom: 0 }}>
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
        ) : receipts.length === 0 ? (
          <div style={{ padding: 40, textAlign: "center", color: "#4B5563", fontSize: 13 }}>
            {tPage("empty")}
          </div>
        ) : (
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                {[
                  tPage("headers.id"),
                  tPage("headers.store"),
                  tPage("headers.supplier"),
                  tPage("headers.expected"),
                  tPage("headers.status"),
                  tPage("headers.items"),
                  tPage("headers.actions"),
                ].map((h) => (
                  <th key={h} style={thStyle}>
                    {h}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {receipts.map((r) => (
                <tr key={r.id}>
                  <td
                    style={{
                      ...tdStyle,
                      fontFamily: "monospace",
                      fontSize: 11,
                      color: "#4B5563",
                    }}
                  >
                    {r.id.slice(0, 8)}…
                  </td>
                  <td style={{ ...tdStyle, color: "#E8EDF5", fontWeight: 500 }}>
                    {r.destinationStoreName}
                  </td>
                  <td style={tdStyle}>{r.supplierName ?? "—"}</td>
                  <td style={tdStyle}>{formatDate(r.expectedAt, intlLocale)}</td>
                  <td style={tdStyle}>
                    <ReceiptStatusBadge status={r.status as ReceiptStatus} />
                  </td>
                  <td style={{ ...tdStyle, fontFamily: "monospace" }}>{r.items.length}</td>
                  <td style={{ ...tdStyle, borderRight: "none" }}>
                    <ActionMenu
                      items={[
                        {
                          label: tPage("actionMenu.view"),
                          icon: <Eye size={13} />,
                          onClick: () => setSelected(r),
                        },
                        {
                          label: tPage("actionMenu.openPage"),
                          icon: <ExternalLink size={13} />,
                          href: `/receipts/${r.id}`,
                        },
                      ]}
                    />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {!isLoading && receipts.length > 0 && (
        <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPageChange={setPage} />
      )}

      {/* Detail drawer */}
      <DetailDrawer
        isOpen={selected !== null}
        onClose={() => setSelected(null)}
        title={t("title")}
        subtitle={
          selected
            ? `${selected.supplierName ?? t("drawer.noSupplier")} → ${selected.destinationStoreName}`
            : ""
        }
      >
        {selected && <ReceiptDetail r={selected} />}
      </DetailDrawer>

      {showCreateModal && (
        <Modal title={t("createForm.modalTitle")} onClose={() => setShowCreateModal(false)} width={720}>
          <CreateReceiptForm
            onSuccess={() => setShowCreateModal(false)}
            onCancel={() => setShowCreateModal(false)}
          />
        </Modal>
      )}
    </div>
  );
}
