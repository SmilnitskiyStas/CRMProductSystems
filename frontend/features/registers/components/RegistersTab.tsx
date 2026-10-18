"use client";

import { useMemo, useState } from "react";
import { Plus } from "lucide-react";
import { toast } from "sonner";
import { useLocale, useTranslations } from "next-intl";
import { Table, type TableColumn } from "@/components/ui/Table";
import { Btn } from "@/components/ui/Btn";
import { useLocations } from "@/features/locations/hooks/useLocations";
import {
  useCreateRegister,
  useIssueActivationCode,
  useRegisterOperators,
  useRegisters,
  useRevokeRegister,
  useUnlockRegister,
  useUpdateRegister,
} from "../hooks/useRegisters";
import type { Register } from "../types";
import { RegisterFormModal, type RegisterFormValues } from "./RegisterFormModal";
import { ConfirmModal, SecretModal, StatusBadge, formatDateTime } from "./shared";

interface CodeState {
  code: string;
  expiresAt: string;
  registerName: string;
  /** True when the code was issued for an already-activated device. */
  reissue: boolean;
}

export function RegistersTab({ storeId }: { storeId?: string }) {
  const t = useTranslations("Dashboard.registers");
  const locale = useLocale();
  const { data: registers = [], isLoading } = useRegisters(storeId);
  const { data: operators = [] } = useRegisterOperators(storeId);
  const { data: stores = [] } = useLocations();

  const createMut = useCreateRegister();
  const updateMut = useUpdateRegister();
  const issueMut = useIssueActivationCode();
  const revokeMut = useRevokeRegister();
  const unlockMut = useUnlockRegister();

  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<Register | null>(null);
  const [code, setCode] = useState<CodeState | null>(null);
  const [revoking, setRevoking] = useState<Register | null>(null);
  const [reissuing, setReissuing] = useState<Register | null>(null);

  const operatorByRegister = useMemo(
    () => new Map(operators.map((o) => [o.registerId, o.cashierName])),
    [operators],
  );

  function handleSubmit(values: RegisterFormValues) {
    if (editing) {
      updateMut.mutate(
        { id: editing.id, data: { name: values.name, storeId: values.storeId } },
        {
          onSuccess: () => { toast.success(t("toast.updated")); setEditing(null); },
          onError: (e) => toast.error(e.message),
        },
      );
      return;
    }
    createMut.mutate(
      { storeId: values.storeId, name: values.name, number: values.number, type: values.type },
      {
        onSuccess: (res) => {
          setFormOpen(false);
          setCode({
            code: res.activationCode,
            expiresAt: res.activationCodeExpiresAt,
            registerName: res.register.name,
            reissue: false,
          });
        },
        onError: (e) => toast.error(e.message),
      },
    );
  }

  function issue(reg: Register) {
    setReissuing(null);
    issueMut.mutate(reg.id, {
      onSuccess: (res) =>
        setCode({
          code: res.activationCode,
          expiresAt: res.expiresAt,
          registerName: reg.name,
          reissue: reg.status === "active" || reg.status === "locked",
        }),
      onError: (e) => toast.error(e.message),
    });
  }

  function requestIssue(reg: Register) {
    // Re-issuing for an activated device replaces its token: confirm first.
    if (reg.status === "active" || reg.status === "locked") setReissuing(reg);
    else issue(reg);
  }

  const columns: TableColumn<Register>[] = [
    {
      key: "number",
      header: t("columns.number"),
      width: 70,
      render: (r) => <span style={{ color: "#E8EDF5", fontWeight: 600 }}>#{r.number}</span>,
    },
    { key: "name", header: t("columns.name"), render: (r) => <span style={{ color: "#E8EDF5" }}>{r.name}</span> },
    { key: "store", header: t("columns.store"), render: (r) => r.storeName ?? "—" },
    { key: "type", header: t("columns.type"), render: (r) => t(`type.${r.type}`) },
    { key: "status", header: t("columns.status"), render: (r) => <StatusBadge status={r.status} /> },
    { key: "lastSeen", header: t("columns.lastSeen"), render: (r) => formatDateTime(r.lastSeenAt, locale) },
    { key: "appVersion", header: t("columns.appVersion"), render: (r) => r.appVersion ?? "—" },
    { key: "operator", header: t("columns.operator"), render: (r) => operatorByRegister.get(r.id) ?? "—" },
    {
      key: "actions",
      header: "",
      render: (r) => {
        if (r.status === "revoked") return null;
        return (
          <div style={{ display: "flex", gap: 6, justifyContent: "center", flexWrap: "wrap" }}>
            <Btn size="sm" variant="ghost" onClick={() => setEditing(r)}>{t("actions.edit")}</Btn>
            <Btn size="sm" onClick={() => requestIssue(r)} disabled={issueMut.isPending}>
              {t("actions.issueCode")}
            </Btn>
            {r.status === "locked" && (
              <Btn
                size="sm"
                variant="success"
                disabled={unlockMut.isPending}
                onClick={() =>
                  unlockMut.mutate(r.id, {
                    onSuccess: () => toast.success(t("toast.unlocked")),
                    onError: (e) => toast.error(e.message),
                  })
                }
              >
                {t("actions.unlock")}
              </Btn>
            )}
            <Btn size="sm" variant="danger" onClick={() => setRevoking(r)}>{t("actions.revoke")}</Btn>
          </div>
        );
      },
    },
  ];

  return (
    <div>
      <div style={{ display: "flex", justifyContent: "flex-end", marginBottom: 14 }}>
        <Btn icon={<Plus size={15} />} onClick={() => setFormOpen(true)}>{t("actions.create")}</Btn>
      </div>

      <Table
        columns={columns}
        rows={registers}
        rowKey={(r) => r.id}
        isLoading={isLoading}
        minWidth={980}
        emptyMessage={t("empty")}
      />

      {(formOpen || editing) && (
        <RegisterFormModal
          register={editing ?? undefined}
          stores={stores}
          defaultStoreId={storeId}
          isPending={createMut.isPending || updateMut.isPending}
          error={(editing ? updateMut.error : createMut.error)?.message ?? null}
          onClose={() => { setFormOpen(false); setEditing(null); createMut.reset(); updateMut.reset(); }}
          onSubmit={handleSubmit}
        />
      )}

      {code && (
        <SecretModal
          title={t("code.title", { name: code.registerName })}
          secret={code.code}
          hint={t("code.hint", { expires: formatDateTime(code.expiresAt, locale) })}
          warning={code.reissue ? t("code.reissueWarning") : undefined}
          onClose={() => setCode(null)}
        />
      )}

      {reissuing && (
        <ConfirmModal
          title={t("reissue.title")}
          text={t("reissue.text", { name: reissuing.name })}
          confirmLabel={t("reissue.confirm")}
          danger
          onConfirm={() => issue(reissuing)}
          onClose={() => setReissuing(null)}
        />
      )}

      {revoking && (
        <ConfirmModal
          title={t("revoke.title")}
          text={t("revoke.text", { name: revoking.name })}
          confirmLabel={t("revoke.confirm")}
          danger
          isPending={revokeMut.isPending}
          onConfirm={() =>
            revokeMut.mutate(revoking.id, {
              onSuccess: () => { toast.success(t("toast.revoked")); setRevoking(null); },
              onError: (e) => toast.error(e.message),
            })
          }
          onClose={() => setRevoking(null)}
        />
      )}
    </div>
  );
}
