"use client";

// Заявки на співпрацю в кабінеті постачальника (TASK-318): фільтр-таби,
// таблиця заявок і дії за статусом (approve → договір → Вчасно / mark-signed →
// active → terminate).

import { useState } from "react";
import { useRouter } from "next/navigation";
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

const FILTER_TABS: { key: FilterTab; label: string }[] = [
  { key: "all", label: "Всі" },
  { key: "pending", label: "Нові" },
  { key: "awaiting_signature", label: "Очікують підписання" },
  { key: "active", label: "Активні" },
  { key: "rejected", label: "Відхилені" },
];

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

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString("uk-UA", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function CooperationRequestsTab() {
  const router = useRouter();
  const [filter, setFilter] = useState<FilterTab>("all");
  const { data: requests = [], isLoading } = useCooperationRequests(
    filter === "all" ? undefined : filter
  );

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
      onSuccess: () => toast.success("Заявку схвалено — договір згенеровано"),
      onError: (err) => {
        // 400 «Спочатку заповніть реквізити договору…» → підказка з переходом
        if (err.message.toLowerCase().includes("реквізит")) {
          toast.error(err.message, {
            action: {
              label: "Реквізити",
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
              Схвалити
            </Btn>
            <Btn size="sm" variant="danger" onClick={() => setRejectTarget(agreement)}>
              Відхилити
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
              Договір
            </Btn>
            <Btn
              size="sm"
              icon={<Send size={13} />}
              disabled={sendToVchasno.isPending}
              onClick={() =>
                sendToVchasno.mutate(agreement.id, {
                  onSuccess: () => toast.success("Договір надіслано у Вчасно"),
                  // 400 «Інтеграцію Вчасно не налаштовано.» — показуємо як є
                  onError: (err) => toast.error(err.message),
                })
              }
            >
              Надіслати у Вчасно
            </Btn>
            <Btn
              size="sm"
              variant="ghost"
              icon={<RefreshCw size={13} />}
              disabled={regenerate.isPending}
              onClick={() =>
                regenerate.mutate(agreement.id, {
                  onSuccess: () => toast.success("Договір перегенеровано"),
                  onError: (err) => toast.error(err.message),
                })
              }
            >
              Перегенерувати
            </Btn>
            <Btn
              size="sm"
              variant="success"
              disabled={markSigned.isPending}
              onClick={() =>
                markSigned.mutate(agreement.id, {
                  onSuccess: () => toast.success("Співпрацю активовано"),
                  onError: (err) => toast.error(err.message),
                })
              }
            >
              Позначити підписаним
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
              Договір
            </Btn>
            <Btn
              size="sm"
              variant="ghost"
              icon={<RefreshCw size={13} />}
              disabled={regenerate.isPending}
              onClick={() =>
                regenerate.mutate(agreement.id, {
                  onSuccess: () => toast.success("Договір перегенеровано"),
                  onError: (err) => toast.error(err.message),
                })
              }
            >
              Перегенерувати
            </Btn>
            <Btn size="sm" variant="danger" onClick={() => setTerminateTarget(agreement)}>
              Розірвати
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
        {FILTER_TABS.map((t) => (
          <button key={t.key} style={tabStyle(t.key)} onClick={() => setFilter(t.key)}>
            {t.label}
          </button>
        ))}
      </div>

      {isLoading && (
        <div style={{ color: "#4B5563", fontSize: 13, padding: "16px 0" }}>Завантаження...</div>
      )}

      {!isLoading && requests.length === 0 && (
        <div style={{ textAlign: "center", padding: "40px 0", color: "#4B5563", fontSize: 14 }}>
          {filter === "all"
            ? "Заявок на співпрацю ще немає."
            : "Немає заявок у цьому статусі."}
        </div>
      )}

      {!isLoading && requests.length > 0 && (
        <div style={{ overflowX: "auto" }}>
          <table style={{ width: "100%", borderCollapse: "collapse" }}>
            <thead>
              <tr>
                <th style={headerCellStyle}>Клієнт</th>
                <th style={headerCellStyle}>Повідомлення</th>
                <th style={headerCellStyle}>Дата заявки</th>
                <th style={headerCellStyle}>Статус</th>
                <th style={headerCellStyle}>Дії</th>
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
                        {a.vchasnoDocumentId && " · Вчасно"}
                      </div>
                    )}
                    {a.signingMethod && (
                      <div style={{ color: "#60A5FA", fontSize: 11, fontWeight: 400, marginTop: 4 }}>
                        Клієнт обрав:{" "}
                        {a.signingMethod === "vchasno"
                          ? `Вчасно (${a.signingEmail})`
                          : "Фізичне підписання"}
                      </div>
                    )}
                  </td>
                  <td style={{ ...cellStyle, color: "#9CA3AF", maxWidth: 320 }}>
                    {a.requestMessage ?? "—"}
                    {a.rejectionReason && (
                      <div style={{ color: "#F87171", fontSize: 11, marginTop: 4 }}>
                        Причина: {a.rejectionReason}
                      </div>
                    )}
                  </td>
                  <td style={{ ...cellStyle, color: "#9CA3AF", whiteSpace: "nowrap" }}>
                    {formatDate(a.requestedAt)}
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
          title={`Відхилити заявку від «${rejectTarget.clientName}»?`}
          label="Причина відмови"
          confirmLabel="Відхилити"
          required
          pending={reject.isPending}
          onConfirm={(reason) =>
            reject.mutate(
              { id: rejectTarget.id, reason },
              {
                onSuccess: () => {
                  toast.success("Заявку відхилено");
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
          title={`Розірвати співпрацю з «${terminateTarget.clientName}»?`}
          label="Причина розірвання"
          confirmLabel="Розірвати"
          pending={terminate.isPending}
          onConfirm={(reason) =>
            terminate.mutate(
              { id: terminateTarget.id, reason: reason || undefined },
              {
                onSuccess: () => {
                  toast.success("Співпрацю розірвано");
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
