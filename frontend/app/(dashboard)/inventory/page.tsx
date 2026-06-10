"use client";

import { useState } from "react";
import { Plus } from "lucide-react";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { ProductForm } from "@/features/inventory/components/ProductForm";
import { ProductsTable } from "@/features/inventory/components/ProductsTable";
import {
  useCreateProduct,
  useDeleteProduct,
  useProducts,
  useUpdateProduct,
} from "@/features/inventory/hooks/useProducts";
import type { CreateProductPayload, Product, UpdateProductPayload } from "@/features/inventory/types";

export default function InventoryPage() {
  const [formOpen, setFormOpen] = useState(false);
  const [editingProduct, setEditingProduct] = useState<Product | null>(null);

  const { data: products = [], isLoading, isError } = useProducts();
  const createProduct = useCreateProduct();
  const updateProduct = useUpdateProduct();
  const deleteProduct = useDeleteProduct();

  const openCreate = () => {
    setEditingProduct(null);
    setFormOpen(true);
  };

  const openEdit = (product: Product) => {
    setEditingProduct(product);
    setFormOpen(true);
  };

  const handleClose = () => {
    setFormOpen(false);
    setEditingProduct(null);
  };

  const handleCreate = (payload: CreateProductPayload) => {
    createProduct.mutate(payload, {
      onSuccess: () => {
        toast.success("Товар додано");
        handleClose();
      },
      onError: (err) => toast.error(err.message),
    });
  };

  const handleUpdate = (id: string, payload: UpdateProductPayload) => {
    updateProduct.mutate(
      { id, payload },
      {
        onSuccess: () => {
          toast.success("Товар оновлено");
          handleClose();
        },
        onError: (err) => toast.error(err.message),
      },
    );
  };

  const handleDelete = (id: string) => {
    deleteProduct.mutate(id, {
      onSuccess: () => toast.success("Товар видалено"),
      onError: (err) => toast.error(err.message),
    });
  };

  if (isError) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 13 }}>
        Помилка завантаження каталогу. Перевірте підключення до API.
      </div>
    );
  }

  return (
    <div style={{ padding: "28px 32px" }}>
      {/* Header */}
      <div
        style={{
          display: "flex",
          alignItems: "flex-start",
          justifyContent: "space-between",
          marginBottom: 28,
        }}
      >
        <div>
          <h1 style={{ color: "#E8EDF5", fontSize: 22, fontWeight: 700, margin: 0 }}>
            Каталог товарів
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {isLoading ? "Завантаження…" : `${products.length} товар${products.length === 1 ? "" : "ів"}`}
          </p>
        </div>
        <Btn icon={<Plus size={15} />} onClick={openCreate}>
          Додати товар
        </Btn>
      </div>

      {isLoading ? (
        <p style={{ color: "#4B5563", fontSize: 13 }}>Завантаження…</p>
      ) : (
        <ProductsTable
          products={products}
          onEdit={openEdit}
          onDelete={handleDelete}
          isDeleting={deleteProduct.isPending}
        />
      )}

      <ProductForm
        open={formOpen}
        product={editingProduct}
        isPending={createProduct.isPending || updateProduct.isPending}
        onClose={handleClose}
        onCreate={handleCreate}
        onUpdate={handleUpdate}
      />
    </div>
  );
}
