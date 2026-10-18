"use client";

import { useState } from "react";
import { toast } from "sonner";
import { useLocale, useTranslations } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import { Btn } from "@/components/ui/Btn";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useClearPin, useGeneratePin, usePosEmployees } from "../hooks/useRegisters";
import type { PosEmployee } from "../types";
import { ConfirmModal, SecretModal, formatDateTime, inputStyle, labelStyle } from "./shared";

export function PinsTab({ storeId: globalStoreId }: { storeId?: string }) {
  const t = useTranslations("Dashboard.registers.pins");
  const tRole = useTranslations("Dashboard.registers.roles");
  const locale = useLocale();
  const { data: stores = [] } = useLocations();

  // PINs are unique per store, so exactly one store is always in play.
  const [pickedStore, setPickedStore] = useState<string | undefined>(undefined);
  const storeId = pickedStore ?? globalStoreId ?? stores[0]?.id;

  const { data: employees = [], isLoading } = usePosEmployees(storeId);
  const generate = useGeneratePin();
  const clear = useClearPin();

  const [issued, setIssued] = useState<{ name: string; pin: string } | null>(null);
  const [resetting, setResetting] = useState<PosEmployee | null>(null);

  function doGenerate(emp: PosEmployee) {
    setResetting(null);
    generate.mutate(emp.id, {
      onSuccess: (res) => setIssued({ name: emp.fullName, pin: res.pin }),
      onError: (e) => toast.error(e.message),
    });
  }

  const columns: TableColumn<PosEmployee>[] = [
    { key: "name", header: t("columns.name"), render: (e) => <span style={{ color: "#E8EDF5" }}>{e.fullName}</span> },
    { key: "role", header: t("columns.role"), render: (e) => tRole.has(e.role) ? tRole(e.role) : e.role },
    {
      key: "pin",
      header: t("columns.pin"),
      render: (e) =>
        e.pinLockedOut ? (
          <span style={{ color: "#FBBF24" }}>{t("lockedOut")}</span>
        ) : e.hasPin ? (
          <span style={{ color: "#4ADE80" }}>{t("set")}</span>
        ) : (
          <span style={{ color: "#6B7280" }}>{t("notSet")}</span>
        ),
    },
    { key: "pinSetAt", header: t("columns.pinSetAt"), render: (e) => formatDateTime(e.pinSetAt, locale) },
    {
      key: "actions",
      header: "",
      render: (e) => (
        <div style={{ display: "flex", gap: 6, justifyContent: "center" }}>
          {e.hasPin ? (
            <>
              <Btn size="sm" disabled={generate.isPending} onClick={() => setResetting(e)}>{t("regenerate")}</Btn>
              <Btn
                size="sm"
                variant="danger"
                disabled={clear.isPending}
                onClick={() =>
                  clear.mutate(e.id, {
                    onSuccess: () => toast.success(t("cleared")),
                    onError: (err) => toast.error(err.message),
                  })
                }
              >
                {t("clear")}
              </Btn>
            </>
          ) : (
            <Btn size="sm" disabled={generate.isPending} onClick={() => doGenerate(e)}>{t("generate")}</Btn>
          )}
        </div>
      ),
    },
  ];

  return (
    <div>
      <div style={{ maxWidth: 320, marginBottom: 14 }}>
        <label style={labelStyle}>{t("store")}</label>
        <select style={inputStyle} value={storeId ?? ""} onChange={(e) => setPickedStore(e.target.value)}>
          {stores.map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
      </div>
      <p style={{ color: "#6B7280", fontSize: 12, margin: "0 0 14px" }}>{t("hint")}</p>

      <Table
        columns={columns}
        rows={employees}
        rowKey={(e) => e.id}
        isLoading={isLoading}
        minWidth={700}
        emptyMessage={t("empty")}
      />

      {issued && (
        <SecretModal
          title={t("pinTitle", { name: issued.name })}
          secret={issued.pin}
          hint={t("pinHint")}
          onClose={() => setIssued(null)}
        />
      )}

      {resetting && (
        <ConfirmModal
          title={t("resetTitle")}
          text={t("resetText", { name: resetting.fullName })}
          confirmLabel={t("regenerate")}
          isPending={generate.isPending}
          onConfirm={() => doGenerate(resetting)}
          onClose={() => setResetting(null)}
        />
      )}
    </div>
  );
}
