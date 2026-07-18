"use client";

// Заявки на співпрацю в кабінеті постачальника (TASK-318): фільтр-таби,
// таблиця заявок і дії за статусом (approve → договір → Вчасно / mark-signed →
// active → terminate).

import { useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations, useLocale } from "next-intl";
import { Eye, RefreshCw, Send } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { ReasonModal } from "@/components/ui/ReasonModal";
import { AgreementStatusBadge } from "@/features/marketplace/components/CooperationBadges";
import type { CooperationAgreementDto, CooperationStatus } from "@/features/marketplace/types";
import { supplierCabinetApi } from "../api/supplier-cabinet-api";
import {
  useApproveCooperationRequest,
  useCooperationRequests,
  useMarkAgreementSigned,
  useRegenerateContract,
  useRejectCooperationRequest,
  useSendToVchasno,
  useTerminateAgreement,
} from "../hooks/useCabinetCooperation";

type FilterTab = "all" | CooperationStatus;

const headerCellStyle: React.CSSProperties = {
  padding: "10px 14px",
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
  textAlign: "left",
  borderBottom: "1px solid #1F2937",
};

const cellStyle: React.CSSProperties = {
  padding: "12px 14px",
  color: "#E8EDF5",
  fontSize: 13,
  borderBottom: "1px solid #1A2235",
  verticalAlign: "top",
};

function formatDate(iso: string, locale: string): string {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function CooperationRequestsTab() {
  const t = useTranslations("Dashboard.supplierCabinet.cooperationRequestsTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const router = useRouter();
  const [filter, setFilter] = useState<FilterTab>("all");
  const { data: requests = [], isLoading } = useCooperationRequests(
    filter === "all" ? undefined : filter
  );

  const FILTER_TABS: { key: FilterTab; label: string }[] = [
    { key: "all", label: t("filterAll") },
    { key: "pending", label: t("filterPending") },
    { key: "awaiting_signature", label: t("filterAwaitingSignature") },
    { key: "active", label: t("filterActive") },
    { key: "rejected", label: t("filterRejected") },
  ];

  const approve = useApproveCooperationRequest();
  const reject = useRejectCooperationRequest();
  const regenerate = useRegenerateContract();
  const sendToVchasno = useSendToVchasno();
  const markSigned = useMarkAgreementSigned();
  const terminate = useTerminateAgreement();

  const [rejectTarget, setRejectTarget] = useState<CooperationAgreementDto | null>(null);
  const [terminateTarget, setTerminateTarget] = useState<CooperationAgreementDto | null>(null);

  function handleApprove(agreement: CooperationAgreementDto) {
    approve.mutate(agreement.id, {
      onSuccess: () => toast.success(t("toastApproved")),
      onError: (err) => {
        // 400 «Спочатку заповніть реквізити договору…» → підказка з переходом
        // (backend error text stays Ukrainian regardless of UI locale — not yet localized, see i18n-rollout-plan.md Block 11)
        if (err.message.toLowerCase().includes("реквізит")) {
          toast.error(err.message, {
            action: {
              label: t("requisitesActionLabel"),
              onClick: () => router.push("/supplier/contract-settings"),
            },
            duration: 8000,
          });
        } else {
          toast.error(err.message);
        }
      },
    });
  }

  function handleViewContract(agreement: CooperationAgreementDto) {
    supplierCabinetApi
      .downloadAgreementContract(agreement.id)
      .catch((err) => toast.error(err.message));
  }

  function actionsFor(agreement: CooperationAgreementDto): React.ReactNode {
    switch (agreement.status) {
      case "pending":
        return (
          <>
            <Btn
              size="sm"
              variant="success"
              disabled={approve.isPending}
              onClick={() => handleApprove(agreement)}
            >
              {t("approveButton")}
            </Btn>
            <Btn size="sm" variant="danger" onClick={() => setRejectTarget(agreement)}>
              {t("rejectButton")}
            </Btn>
          </>
        );
      case "awaiting_signature":
        return (
          <>
            <Btn
              size="sm"
              variant="ghost"
              icon={<Eye size={13} />}
              onClick={() => handleViewContract(agreement)}
            >
              {t("viewContractButton")}
            </Btn>
            <Btn
              size="sm"
              icon={<Send size={13} />}
              disabled={sendToVchasno.isPending}
              onClick={() =>
                sendToVchasno.mutate(agreement.id, {
                  onSuccess: () => toast.success(t("toastSentToVchasno")),
                  // 400 «Інтеграцію Вчасно не налаштовано.» — показуємо як є
                  onError: (err) => toast.error(err.message),
                })
              }
            >
              {t("sendToVchasnoButton")}
            </Btn>
            <Btn
              size="sm"
              variant="ghost"
              icon={<RefreshCw size={13} />}
              disabled={regenerate.isPending}
              onClick={() =>
                regenerate.mutate(agreement.id, {
                  onSuccess: () => toast.success(t("toastRegenerated")),
                  onError: (err) => toast.error(err.message),
                })
              }
            >
              {t("regenerateButton")}
            </Btn>
            <Btn
              size="sm"
              variant="success"
              disabled={markSigned.isPending}
              onClick={() =>
                markSigned.mutate(agreement.id, {
                  onSuccess: () => toast.success(t("toastActivated")),
                  onError: (err) => toast.error(err.message),
                })
              }
            >
              {t("markSignedButton")}
            </Btn>
          </>
        );
      case "active":
        return (
          <>
            <Btn
              size="sm"
              variant="ghost"
              icon={<Eye size={13} />}
              onClick={() => handleViewContract(agreement)}
            >
              {t("viewContractButton")}
            </Btn>
            <Btn size="sm" variant="danger" onClick={() => setTerminateTarget(agreement)}>
              {t("terminateButton")}
            </Btn>
          </>
        );
      default:
        return null;
    }
  }

  const tabStyle = (tab: FilterTab): React.CSSProperties => ({
    padding: "8px 16px",
    background: "transparent",
    border: "none",
    borderBottom: filter === tab ? "2px solid #3B82F6" : "2px solid transparent",
    color: filter === tab ? "#3B82F6" : "#6B7280",
    fontSize: 13,
    fontWeight: filter === tab ? 600 : 400,
    cursor: "pointer",
    marginBottom: -1,
    whiteSpace: "nowrap",
    transition: "color 0.15s",
  });

  return (
    <div>
      <div
        style={{
          borderBottom: "1px solid #1F2937",
          marginBottom: 20,
          display: "flex",
          overflowX: "auto",
        }}
      >
        {FILTER_TABS.map((tab) => (
          <button key={tab.key} style={tabStyle(tab.key)} onClick={() => setFilter(tab.key)}>
            {tab.label}
          </button>
        ))}
      </div>

      {isLoading && (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>{t("loading")}</div>
      )}

      {!isLoading && requests.length === 0 && (
        <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 14 }}>
          {filter === "all"
            ? t("emptyAll")
            : t("emptyFiltered")}
        </div>
      )}

      {!isLoading && requests.length > 0 && (
        <div style={{ overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                <th style={headerCellStyle}>{t("headerClient")}</th>
                <th style={headerCellStyle}>{t("headerMessage")}</th>
                <th style={headerCellStyle}>{t("headerRequestDate")}</th>
                <th style={headerCellStyle}>{t("headerStatus")}</th>
                <th style={headerCellStyle}>{t("headerActions")}</th>
              </tr>
            </thead>
            <tbody>
              {requests.map((a) => (
                <tr key={a.id}>
                  <td style={{ ...cellStyle, fontWeight: 600, whiteSpace: "nowrap" }}>
                    {a.clientName}
                    {a.contractNumber && (
                      <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 400, marginTop: 4 }}>
                        {a.contractNumber}
                        {a.vchasnoDocumentId && t("vchasnoSuffix")}
                      </div>
                    )}
                    {a.signingMethod && (
                      <div style={{ color: "#60A5FA", fontSize: 11, fontWeight: 400, marginTop: 4 }}>
                        {t("clientChoseLabel")}{" "}
                        {a.signingMethod === "vchasno"
                          ? t("vchasnoChoice", { email: a.signingEmail ?? "" })
                          : t("physicalChoice")}
                      </div>
                    )}
                  </td>
                  <td style={{ ...cellStyle, color: "#9CA3AF", maxWidth: 320 }}>
                    {a.requestMessage ?? "—"}
                    {a.rejectionReason && (
                      <div style={{ color: "#F87171", fontSize: 11, marginTop: 4 }}>
                        {t("reasonPrefixLabel", { reason: a.rejectionReason })}
                      </div>
                    )}
                  </td>
                  <td style={{ ...cellStyle, color: "#9CA3AF", whiteSpace: "nowrap" }}>
                    {formatDate(a.requestedAt, intlLocale)}
                  </td>
                  <td style={cellStyle}>
                    <AgreementStatusBadge status={a.status} />
                  </td>
                  <td style={cellStyle}>
                    <div style={{ display: "flex", gap: 8, flexWrap: "wrap" }}>
                      {actionsFor(a)}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {rejectTarget && (
        <ReasonModal
          title={t("rejectModalTitle", { name: rejectTarget.clientName })}
          label={t("rejectModalLabel")}
          confirmLabel={t("rejectModalConfirm")}
          required
          pending={reject.isPending}
          onConfirm={(reason) =>
            reject.mutate(
              { id: rejectTarget.id, reason },
              {
                onSuccess: () => {
                  toast.success(t("toastRejected"));
                  setRejectTarget(null);
                },
                onError: (err) => toast.error(err.message),
              }
            )
          }
          onClose={() => setRejectTarget(null)}
        />
      )}

      {terminateTarget && (
        <ReasonModal
          title={t("terminateModalTitle", { name: terminateTarget.clientName })}
          label={t("terminateModalLabel")}
          confirmLabel={t("terminateModalConfirm")}
          pending={terminate.isPending}
          onConfirm={(reason) =>
            terminate.mutate(
              { id: terminateTarget.id, reason: reason || undefined },
              {
                onSuccess: () => {
                  toast.success(t("toastTerminated"));
                  setTerminateTarget(null);
                },
                onError: (err) => toast.error(err.message),
              }
            )
          }
          onClose={() => setTerminateTarget(null)}
        />
      )}
    </div>
  );
}
