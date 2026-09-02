"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { FolderTree, Plus } from "lucide-react";
import { useMe } from "@/features/auth/hooks/useAuth";
import { Btn } from "@/components/ui/Btn";
import { useProviderCategories } from "@/features/provider/hooks/useProviderCategories";
import { CategoryTreeManager } from "@/features/provider/components/CategoryTreeManager";
import { CategoryFormModal } from "@/features/provider/components/CategoryFormModal";
import type { PlatformCategoryDto } from "@/features/provider/types";

const PROVIDER_ROLES = ["provider", "provider_admin", "provider_agent"];

type ModalState =
  | { kind: "closed" }
  | { kind: "create" }
  | { kind: "create-sub"; parentId: string }
  | { kind: "edit"; category: PlatformCategoryDto };

export default function ProviderCategoriesPage() {
  const t = useTranslations("Dashboard.providerCategories");
  const router = useRouter();
  const { data: me, isLoading: meLoading } = useMe();

  const { data: categories = [], isLoading } = useProviderCategories();
  const [modal, setModal] = useState<ModalState>({ kind: "closed" });

  useEffect(() => {
    if (!meLoading && me && !PROVIDER_ROLES.includes(me.role)) {
      router.replace("/dashboard");
    }
  }, [me, meLoading, router]);

  if (meLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", height: "60vh" }}>
        <div style={{ color: "#4B5563", fontSize: 14 }}>{t("loading")}</div>
      </div>
    );
  }

  if (!me || !PROVIDER_ROLES.includes(me.role)) return null;

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "flex-start", justifyContent: "space-between", marginBottom: 28 }}>
        <div>
          <div style={{ display: "flex", alignItems: "center", gap: 10, marginBottom: 4 }}>
            <div
              style={{
                width: 34,
                height: 34,
                borderRadius: 9,
                background: "linear-gradient(135deg, #7C3AED, #3B82F6)",
                display: "flex",
                alignItems: "center",
                justifyContent: "center",
                flexShrink: 0,
              }}
            >
              <FolderTree size={17} color="#fff" />
            </div>
            <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>{t("title")}</h1>
          </div>
          <p style={{ color: "#4B5563", fontSize: 14, margin: 0 }}>{t("subtitle")}</p>
        </div>

        <Btn icon={<Plus size={15} />} onClick={() => setModal({ kind: "create" })}>
          {t("addButton")}
        </Btn>
      </div>

      {isLoading ? (
        <p style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</p>
      ) : (
        <CategoryTreeManager
          categories={categories}
          onEdit={(category) => setModal({ kind: "edit", category })}
          onAddSub={(parentId) => setModal({ kind: "create-sub", parentId })}
        />
      )}

      {modal.kind !== "closed" && (
        <CategoryFormModal
          category={modal.kind === "edit" ? modal.category : null}
          presetParentId={modal.kind === "create-sub" ? modal.parentId : null}
          allCategories={categories}
          onClose={() => setModal({ kind: "closed" })}
        />
      )}
    </div>
  );
}
