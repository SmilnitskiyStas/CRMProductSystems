"use client";

import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { PageContainer } from "@/components/ui/PageContainer";
import { PageHeader } from "@/components/ui/PageHeader";
import { BackLink } from "@/components/ui/BackLink";
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
    <PageContainer>
      <BackLink onClick={() => router.push("/inventory")}>{tCommon("back")}</BackLink>
      <PageHeader title={t("titleCreate")} />

      <ProductForm
        product={null}
        isPending={createProduct.isPending}
        onCancel={() => router.push("/inventory")}
        onCreate={handleCreate}
        // Create-only page — ProductForm only calls onUpdate when `product` is non-null.
        onUpdate={() => {}}
      />
    </PageContainer>
  );
}
