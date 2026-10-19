"use client";

import { useState } from "react";
import { KeyRound, RefreshCw, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { useLocale, useTranslations } from "next-intl";
import { ActionMenu, type ActionMenuItem } from "@/components/ui/ActionMenu";
import { Table, type TableColumn } from "@/components/ui/Table";
import { useClearPin, useGeneratePin, useManageableStores, usePosEmployees } from "../hooks/useRegisters";
import type { PosEmployee } from "../types";
import { ConfirmModal, SecretModal, formatDateTime } from "./shared";

export function PinsTab({ storeId: globalStoreId }: { storeId?: string }) {
  const t = useTranslations("Dashboard.registers.pins");
  const tRole = useTranslations("Dashboard.registers.roles");
  const tActions = useTranslations("Dashboard.registers.actions");
  const locale = useLocale();
  const { data: stores = [] } = useManageableStores();

  // PINs are unique per store, so exactly one store is always in play: the one chosen in the
  // header store selector, or (when the header shows "all stores") the first manageable one.
  const globalManageable = stores.some((s) => s.id === globalStoreId) ? globalStoreId : undefined;
  const storeId = globalManageable ?? stores[0]?.id;
  const storeName = stores.find((s) => s.id === storeId)?.name;

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
      render: (e) => {
        const items: ActionMenuItem[] = e.hasPin
          ? [
              {
                label: t("regenerate"),
                icon: <RefreshCw size={13} />,
                disabled: generate.isPending,
                onClick: () => setResetting(e),
              },
              { separator: true },
              {
                label: t("clear"),
                icon: <Trash2 size={13} />,
                variant: "danger",
                disabled: clear.isPending,
                onClick: () =>
                  clear.mutate(e.id, {
                    onSuccess: () => toast.success(t("cleared")),
                    onError: (err) => toast.error(err.message),
                  }),
              },
            ]
          : [
              {
                label: t("generate"),
                icon: <KeyRound size={13} />,
                disabled: generate.isPending,
                onClick: () => doGenerate(e),
              },
            ];
        return (
          <div style={{ display: "flex", justifyContent: "center" }}>
            <ActionMenu label={tActions("menu")} items={items} />
          </div>
        );
      },
    },
  ];

  return (
    <div>
      {storeName && (
        <p style={{ color: "#9CA3AF", fontSize: 13, margin: "0 0 6px" }}>
          {t("store")}: <strong style={{ color: "#E8EDF5" }}>{storeName}</strong> · {t("storeFromHeader")}
        </p>
      )}
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
