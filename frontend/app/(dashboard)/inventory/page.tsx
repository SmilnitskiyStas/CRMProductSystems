"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Plus } from "lucide-react";
import { toast } from "sonner";
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
        toast.success("Product added");
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
          toast.success("Product updated");
          handleClose();
        },
        onError: (err) => toast.error(err.message),
      },
    );
  };

  const handleDelete = (id: string) => {
    deleteProduct.mutate(id, {
      onSuccess: () => toast.success("Product deleted"),
      onError: (err) => toast.error(err.message),
    });
  };

  if (isError) {
    return (
      <div className="p-6 text-destructive">
        Failed to load products. Check the API connection and try again.
      </div>
    );
  }

  return (
    <div className="p-6 space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">Product Catalog</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {products.length} product{products.length !== 1 ? "s" : ""}
          </p>
        </div>
        <Button onClick={openCreate}>
          <Plus className="mr-2 h-4 w-4" />
          Add product
        </Button>
      </div>

      {isLoading ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
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
