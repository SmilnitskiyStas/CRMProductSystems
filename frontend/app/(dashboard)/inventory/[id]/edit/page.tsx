"use client";

import { useParams, useRouter } from "next/navigation";
import { ArrowLeft, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { ProductForm } from "@/features/inventory/components/ProductForm";
import { useProduct, useUpdateProduct, useUploadProductImage } from "@/features/inventory/hooks/useProducts";
import type { UpdateProductPayload } from "@/features/inventory/types";

// TASK-718 — product edit moved off the ProductForm modal onto its own route. Mirrors the
// back-button header pattern and loading branch from /inventory/[id]/page.tsx.
export default function EditProductPage() {
  const router = useRouter();
  const { id } = useParams<{ id: string }>();
  const t = useTranslations("Dashboard.inventory.form");
  const tPage = useTranslations("Dashboard.inventory.page");
  const tProductPage = useTranslations("Dashboard.inventory.productPage");
  const tCommon = useTranslations("Common");

  const { data: product, isLoading } = useProduct(id);
  const updateProduct = useUpdateProduct();
  const uploadImage = useUploadProductImage();

  const handleUpdate = (productId: string, payload: UpdateProductPayload) => {
    updateProduct.mutate(
      { id: productId, payload },
      {
        onSuccess: () => {
          toast.success(tPage("toastUpdated"));
          router.push(`/inventory/${productId}`);
        },
        onError: (err) => toast.error(err.message),
      },
    );
  };

  const handleImageUpload = (productId: string, file: File) => {
    uploadImage.mutate({ id: productId, file }, { onError: (err) => toast.error(err.message) });
  };

  if (isLoading) {
    return (
      <div style={{ display: "flex", alignItems: "center", justifyContent: "center", padding: "80px 0" }}>
        <Loader2 size={28} color="#374151" style={{ animation: "spin 1s linear infinite" }} />
        <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
      </div>
    );
  }

  if (!product) {
    return (
      <div style={{ padding: 40, textAlign: "center", color: "#F87171", fontSize: 13 }}>
        {tProductPage("notFound")}
      </div>
    );
  }

  return (
    <div style={{ padding: "24px 32px" }}>
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 20 }}>
        <button
          onClick={() => router.push(`/inventory/${id}`)}
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
          {t("titleEdit")}
        </h1>
      </div>

      <div style={{ maxWidth: 640 }}>
        <ProductForm
          product={product}
          isPending={updateProduct.isPending}
          onCancel={() => router.push(`/inventory/${id}`)}
          // Edit-only page — ProductForm only calls onCreate when `product` is null.
          onCreate={() => {}}
          onUpdate={handleUpdate}
          onImageUpload={handleImageUpload}
        />
      </div>
    </div>
  );
}
