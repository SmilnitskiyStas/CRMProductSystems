"use client";

import { useRouter } from "next/navigation";
import { ArrowLeft } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { ProductForm } from "@/features/inventory/components/ProductForm";
import { useCreateProduct } from "@/features/inventory/hooks/useProducts";
import type { CreateProductPayload } from "@/features/inventory/types";

// TASK-718 — product create moved off the ProductForm modal onto its own route. Mirrors the
// back-button header pattern from /inventory/[id]/page.tsx.
export default function NewProductPage() {
  const router = useRouter();
  const t = useTranslations("Dashboard.inventory.form");
  const tPage = useTranslations("Dashboard.inventory.page");
  const tCommon = useTranslations("Common");

  const createProduct = useCreateProduct();

  const handleCreate = (payload: CreateProductPayload) => {
    createProduct.mutate(payload, {
      onSuccess: (created) => {
        toast.success(tPage("toastCreated"));
        router.push(`/inventory/${created.id}`);
      },
      onError: (err) => toast.error(err.message),
    });
  };

  return (
    <div style={{ padding: "24px 32px" }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 20 }}>
        <button
          onClick={() => router.push("/inventory")}
          style={{
            background: "transparent", border: "none",
            color: "#6B7280", cursor: "pointer",
            display: "flex", alignItems: "center", gap: 4,
            fontSize: 13, padding: 0, flexShrink: 0,
          }}
        >
          <ArrowLeft size={15} /> {tCommon("back")}
        </button>
        <h1 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>
          {t("titleCreate")}
        </h1>
      </div>

      <div style={{ maxWidth: 640 }}>
        <ProductForm
          product={null}
          isPending={createProduct.isPending}
          onCancel={() => router.push("/inventory")}
          onCreate={handleCreate}
          // Create-only page — ProductForm only calls onUpdate when `product` is non-null.
          onUpdate={() => {}}
        />
      </div>
    </div>
  );
}
