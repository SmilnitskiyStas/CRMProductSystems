"use client";

import { useState } from "react";
import { CreditCard, CheckCircle, Clock, Hash } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { ShiftStatusCard } from "@/features/pos/components/ShiftStatusCard";
import { SalesTable } from "@/features/pos/components/SalesTable";
import { SaleDetailDrawer } from "@/features/pos/components/SaleDetailDrawer";
import { OpenShiftDialog } from "@/features/pos/components/OpenShiftDialog";
import { CloseShiftDialog } from "@/features/pos/components/CloseShiftDialog";
import { CashReconciliationSummary } from "@/features/pos/components/CashReconciliationSummary";
import {
  useCurrentShift,
  useShiftSales,
  useOpenShift,
  useCloseShift,
} from "@/features/pos/hooks/usePos";
import { ApiError } from "@/lib/api";
import type { SaleDto } from "@/features/pos/types";

function formatDateTime(iso: string, intlLocale: string): string {
  return new Date(iso).toLocaleString(intlLocale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export default function PosPage() {
  const t = useTranslations("Dashboard.pos.page");
  const tCommon = useTranslations("Common");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: shift, isLoading: shiftLoading, error: shiftError } = useCurrentShift();
  const { data: salesData, isLoading: salesLoading } = useShiftSales(
    shift?.status === "Open" || shift?.status === "Closed" ? shift.shiftId : undefined,
  );

  const openShift = useOpenShift();
  const closeShift = useCloseShift();

  const [openDialogVisible, setOpenDialogVisible] = useState(false);
  const [closeDialogVisible, setCloseDialogVisible] = useState(false);
  const [closeError, setCloseError] = useState<string | null>(null);
  const [selectedSale, setSelectedSale] = useState<SaleDto | null>(null);

  const noShift =
    !shiftLoading && (shiftError instanceof ApiError && shiftError.status === 404 || !shift);
  const isOpen = shift?.status === "Open";
  const isClosed = shift?.status === "Closed";

  function handleOpenShift(storeId: string, openingCash?: number) {
    openShift.mutate(
      { storeId, openingCash },
      {
        onSuccess: () => setOpenDialogVisible(false),
      },
    );
  }

  function handleConfirmClose(actualClosingCash?: number) {
    setCloseError(null);
    closeShift.mutate(
      { actualClosingCash },
      {
        onSuccess: () => setCloseDialogVisible(false),
        onError: (err) => {
          setCloseError(err instanceof ApiError ? err.message : t("closeFailedDefault"));
        },
      },
    );
  }

  // ── Loading ───────────────────────────────────────────────────────────────
  if (shiftLoading) {
    return (
      <div style={{ padding: 32, color: "#4B5563", fontSize: 13, textAlign: "center" }}>
        {tCommon("loading")}
      </div>
    );
  }

  return (
    <div style={{ padding: 32, maxWidth: 1200, margin: "0 auto" }}>
      {/* Page header */}
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          marginBottom: 24,
          flexWrap: "wrap",
          gap: 12,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <CreditCard size={22} color="#3B82F6" />
          <h1 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
        </div>
        {noShift && (
          <Btn
            variant="success"
            icon={<CreditCard size={14} />}
            onClick={() => setOpenDialogVisible(true)}
          >
            {t("openShift")}
          </Btn>
        )}
      </div>

      {/* ── No open shift ─────────────────────────────────────────────────── */}
      {noShift && (
        <div
          style={{
            display: "flex",
            flexDirection: "column",
            alignItems: "center",
            justifyContent: "center",
            padding: "80px 24px",
            background: "#161B26",
            border: "1px solid #1F2937",
            borderRadius: 12,
            gap: 16,
            textAlign: "center",
          }}
        >
          <div
            style={{
              width: 72,
              height: 72,
              borderRadius: "50%",
              background: "#1F2937",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
            }}
          >
            <CreditCard size={32} color="#4B5563" />
          </div>
          <div>
            <div style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, marginBottom: 6 }}>
              {t("noShiftTitle")}
            </div>
            <div style={{ color: "#4B5563", fontSize: 13 }}>
              {t("noShiftSubtitle")}
            </div>
          </div>
          <Btn
            variant="success"
            icon={<CreditCard size={14} />}
            onClick={() => setOpenDialogVisible(true)}
          >
            {t("openShift")}
          </Btn>
        </div>
      )}

      {/* ── Open shift ────────────────────────────────────────────────────── */}
      {shift && isOpen && (
        <>
          <ShiftStatusCard
            shift={shift}
            onClose={() => setCloseDialogVisible(true)}
            isClosing={closeShift.isPending}
          />

          <div style={{ marginTop: 24 }}>
            <div
              style={{
                color: "#9CA3AF",
                fontSize: 12,
                fontWeight: 600,
                textTransform: "uppercase",
                letterSpacing: "0.05em",
                marginBottom: 12,
              }}
            >
              {t("currentShiftSales")}
            </div>
            <SalesTable
              sales={salesData?.items}
              totalAmount={salesData?.totalAmount ?? 0}
              isLoading={salesLoading}
              onSelectSale={setSelectedSale}
            />
          </div>
        </>
      )}

      {/* ── Closed shift — Z-report ───────────────────────────────────────── */}
      {shift && isClosed && (
        <>
          {/* Z-report summary card */}
          <div
            style={{
              background: "#161B26",
              border: "1px solid #1F2937",
              borderRadius: 12,
              padding: 24,
              marginBottom: 24,
            }}
          >
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: 8,
                marginBottom: 20,
              }}
            >
              <CheckCircle size={18} color="#22c55e" />
              <span style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700 }}>
                {t("zReportTitle")}
              </span>
              {shift.shiftNumber != null && (
                <span
                  style={{
                    background: "#1F2937",
                    borderRadius: 5,
                    padding: "2px 8px",
                    color: "#9CA3AF",
                    fontSize: 12,
                    fontFamily: "monospace",
                  }}
                >
                  #{shift.shiftNumber}
                </span>
              )}
            </div>

            <div style={{ display: "flex", gap: 40, flexWrap: "wrap" }}>
              <ZReportField
                icon={<Clock size={13} color="#4B5563" />}
                label={t("zReportOpened")}
                value={formatDateTime(shift.openedAt, intlLocale)}
              />
              {shift.closedAt && (
                <ZReportField
                  icon={<Clock size={13} color="#4B5563" />}
                  label={t("zReportClosed")}
                  value={formatDateTime(shift.closedAt, intlLocale)}
                />
              )}
              <ZReportField
                icon={<Hash size={13} color="#4B5563" />}
                label={t("zReportTotal")}
                value={
                  <span style={{ color: "#34d399", fontSize: 18, fontWeight: 700 }}>
                    {shift.totalSales.toFixed(2)} ₴
                  </span>
                }
              />
              <ZReportField
                icon={<CreditCard size={13} color="#4B5563" />}
                label={t("zReportPrroStatus")}
                value={
                  <span style={{ fontFamily: "monospace", fontSize: 12, color: "#9CA3AF" }}>
                    {shift.fiscalStatus || "—"}
                  </span>
                }
              />
            </div>

            <CashReconciliationSummary shift={shift} />
          </div>

          {/* Sales history for the closed shift */}
          <div>
            <div
              style={{
                color: "#9CA3AF",
                fontSize: 12,
                fontWeight: 600,
                textTransform: "uppercase",
                letterSpacing: "0.05em",
                marginBottom: 12,
              }}
            >
              {t("closedShiftSales")}
            </div>
            <SalesTable
              sales={salesData?.items}
              totalAmount={salesData?.totalAmount ?? 0}
              isLoading={salesLoading}
              onSelectSale={setSelectedSale}
            />
          </div>

          <div style={{ marginTop: 20, display: "flex", justifyContent: "flex-end" }}>
            <Btn
              variant="success"
              icon={<CreditCard size={14} />}
              onClick={() => setOpenDialogVisible(true)}
            >
              {t("openNewShift")}
            </Btn>
          </div>
        </>
      )}

      {/* ── Shift in transitional state ───────────────────────────────────── */}
      {shift && !isOpen && !isClosed && (
        <ShiftStatusCard
          shift={shift}
          onClose={() => setCloseDialogVisible(true)}
          isClosing={closeShift.isPending}
        />
      )}

      {/* ── Dialogs / Drawers ─────────────────────────────────────────────── */}
      <OpenShiftDialog
        isOpen={openDialogVisible}
        onClose={() => setOpenDialogVisible(false)}
        onConfirm={handleOpenShift}
        isLoading={openShift.isPending}
      />

      <CloseShiftDialog
        isOpen={closeDialogVisible}
        onClose={() => {
          setCloseDialogVisible(false);
          setCloseError(null);
        }}
        onConfirm={handleConfirmClose}
        isLoading={closeShift.isPending}
        error={closeError}
      />

      <SaleDetailDrawer sale={selectedSale} onClose={() => setSelectedSale(null)} />
    </div>
  );
}

function ZReportField({
  icon,
  label,
  value,
}: {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          gap: 4,
          color: "#4B5563",
          fontSize: 11,
          fontWeight: 600,
          textTransform: "uppercase",
          letterSpacing: "0.05em",
          marginBottom: 4,
        }}
      >
        {icon}
        {label}
      </div>
      <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 500 }}>{value}</div>
    </div>
  );
}
