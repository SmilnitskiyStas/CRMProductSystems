"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useRegisterSessions, useRegisterShifts, useRegisters } from "../hooks/useRegisters";
import type { RegisterSession, RegisterShift } from "../types";
import { formatDateTime, formatMoney, inputStyle, labelStyle } from "./shared";

const PAGE_SIZE = 15;

export function JournalTab({ storeId }: { storeId?: string }) {
  const t = useTranslations("Dashboard.registers.journal");
  const locale = useLocale();
  const { data: registers = [] } = useRegisters(storeId);

  const [pickedId, setPickedId] = useState<string | undefined>(undefined);
  const registerId = registers.some((r) => r.id === pickedId) ? pickedId : registers[0]?.id;

  const [sessionsPage, setSessionsPage] = useState(1);
  const [shiftsPage, setShiftsPage] = useState(1);
  const sessions = useRegisterSessions(registerId, sessionsPage, PAGE_SIZE);
  const shifts = useRegisterShifts(registerId, shiftsPage, PAGE_SIZE);

  function pick(id: string) {
    setPickedId(id);
    setSessionsPage(1);
    setShiftsPage(1);
  }

  const sessionColumns: TableColumn<RegisterSession>[] = [
    { key: "cashier", header: t("sessions.cashier"), render: (s) => <span style={{ color: "#E8EDF5" }}>{s.cashierName}</span> },
    { key: "start", header: t("sessions.start"), render: (s) => formatDateTime(s.startedAt, locale) },
    { key: "end", header: t("sessions.end"), render: (s) => (s.endedAt ? formatDateTime(s.endedAt, locale) : t("sessions.open")) },
    { key: "reason", header: t("sessions.reason"), render: (s) => (s.endReason ? t(`reasons.${s.endReason}`) : "—") },
  ];

  const shiftColumns: TableColumn<RegisterShift>[] = [
    { key: "number", header: t("shifts.number"), render: (s) => <span style={{ color: "#E8EDF5" }}>#{s.shiftNumber}</span> },
    { key: "cashier", header: t("shifts.cashier"), render: (s) => s.cashierName ?? "—" },
    { key: "opened", header: t("shifts.opened"), render: (s) => formatDateTime(s.openedAt, locale) },
    { key: "closed", header: t("shifts.closed"), render: (s) => (s.closedAt ? formatDateTime(s.closedAt, locale) : t("shifts.open")) },
    { key: "receipts", header: t("shifts.receipts"), render: (s) => s.receipts },
    { key: "total", header: t("shifts.total"), render: (s) => formatMoney(s.totalSales) },
  ];

  const totalPages = (total?: number) => Math.max(1, Math.ceil((total ?? 0) / PAGE_SIZE));

  if (registers.length === 0) {
    return <p style={{ color: "#6B7280", fontSize: 13 }}>{t("noRegisters")}</p>;
  }

  return (
    <div>
      <div style={{ maxWidth: 360, marginBottom: 18 }}>
        <label style={labelStyle}>{t("register")}</label>
        <select style={inputStyle} value={registerId ?? ""} onChange={(e) => pick(e.target.value)}>
          {registers.map((r) => (
            <option key={r.id} value={r.id}>#{r.number} {r.name}{r.storeName ? ` — ${r.storeName}` : ""}</option>
          ))}
        </select>
      </div>

      <h3 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700, margin: "0 0 10px" }}>{t("sessions.title")}</h3>
      <Table
        columns={sessionColumns}
        rows={sessions.data?.items ?? []}
        rowKey={(s) => s.id}
        isLoading={sessions.isLoading}
        page={sessionsPage}
        totalPages={totalPages(sessions.data?.total)}
        totalCount={sessions.data?.total ?? 0}
        onPageChange={setSessionsPage}
        minWidth={640}
        emptyMessage={t("sessions.empty")}
      />

      <h3 style={{ color: "#E8EDF5", fontSize: 14, fontWeight: 700, margin: "28px 0 10px" }}>{t("shifts.title")}</h3>
      <Table
        columns={shiftColumns}
        rows={shifts.data?.items ?? []}
        rowKey={(s) => s.id}
        isLoading={shifts.isLoading}
        page={shiftsPage}
        totalPages={totalPages(shifts.data?.total)}
        totalCount={shifts.data?.total ?? 0}
        onPageChange={setShiftsPage}
        minWidth={760}
        emptyMessage={t("shifts.empty")}
      />
    </div>
  );
}
