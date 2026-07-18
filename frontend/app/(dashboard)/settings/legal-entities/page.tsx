"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { useMe } from "@/features/auth/hooks/useAuth";
import { canManageLegalEntities } from "@/lib/roles";
import {
  useLegalEntities,
  useCreateLegalEntity,
  useUpdateLegalEntity,
  useDeactivateLegalEntity,
} from "@/features/legal-entities/hooks/useLegalEntities";
import { LegalEntityFormDialog } from "@/features/legal-entities/components/LegalEntityFormDialog";
import { LegalEntitiesList } from "@/features/legal-entities/components/LegalEntitiesList";
import type { LegalEntityDto } from "@/features/legal-entities/types";

export default function LegalEntitiesPage() {
  const t = useTranslations("Dashboard.legalEntities.page");
  const { data: me } = useMe();
  const canManage = canManageLegalEntities(me?.role, me?.permissions);

  const { data: entities, isLoading } = useLegalEntities();
  const [dialog, setDialog] = useState<"create" | LegalEntityDto | null>(null);

  const create = useCreateLegalEntity();
  const updateId = dialog && dialog !== "create" ? dialog.id : "";
  const update = useUpdateLegalEntity(updateId);
  const deactivate = useDeactivateLegalEntity();

  function handleSubmit(values: {
    legalName: string;
    edrpou: string | null;
    legalAddress: string | null;
    directorName: string | null;
    phone: string | null;
    email: string | null;
    iban: string | null;
    bankName: string | null;
    isVatPayer: boolean;
    isActive: boolean;
  }) {
    if (dialog === "create") {
      create.mutate(
        {
          legalName: values.legalName,
          edrpou: values.edrpou,
          legalAddress: values.legalAddress,
          directorName: values.directorName,
          phone: values.phone,
          email: values.email,
          iban: values.iban,
          bankName: values.bankName,
          isVatPayer: values.isVatPayer,
        },
        {
          onSuccess: () => {
            toast.success(t("createSuccess"));
            setDialog(null);
          },
          onError: (e) => toast.error(t("errorPrefix", { message: e.message })),
        }
      );
    } else if (dialog) {
      update.mutate(values, {
        onSuccess: () => {
          toast.success(t("updateSuccess"));
          setDialog(null);
        },
        onError: (e) => toast.error(t("errorPrefix", { message: e.message })),
      });
    }
  }

  function handleDeactivate(entity: LegalEntityDto) {
    if (!confirm(t("deactivateConfirm", { name: entity.legalName }))) return;
    deactivate.mutate(entity.id, {
      onSuccess: () => toast.success(t("deactivateSuccess")),
      onError: (e) => toast.error(t("errorPrefix", { message: e.message })),
    });
  }

  const isPending = create.isPending || update.isPending;

  return (
    <div style={{ padding: "28px 32px", display: "flex", flexDirection: "column", gap: 20 }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 16 }}>
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            {t("title")}
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {t("subtitle")}
          </p>
        </div>
        {canManage && (
          <Btn icon={<Plus size={15} />} onClick={() => setDialog("create")}>
            {t("newButton")}
          </Btn>
        )}
      </div>

      {/* List */}
      <LegalEntitiesList
        entities={entities ?? []}
        isLoading={isLoading}
        canManage={canManage}
        onEdit={(entity) => setDialog(entity)}
        onDeactivate={handleDeactivate}
      />

      {/* Dialog */}
      {dialog !== null && canManage && (
        <LegalEntityFormDialog
          entity={dialog === "create" ? null : dialog}
          isPending={isPending}
          onClose={() => setDialog(null)}
          onSubmit={handleSubmit}
        />
      )}
    </div>
  );
}
