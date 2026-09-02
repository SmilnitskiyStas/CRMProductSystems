"use client";

import { useEffect, useRef, useState } from "react";
import { Plus } from "lucide-react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { Pagination } from "@/components/ui/Pagination";
import { ProductForm } from "@/features/inventory/components/ProductForm";
import { ProductsTable } from "@/features/inventory/components/ProductsTable";
import {
  useCreateProduct,
  useDeleteProduct,
  useProductsPaged,
  useUpdateProduct,
} from "@/features/inventory/hooks/useProducts";
import { useCategories } from "@/features/inventory/hooks/useCategories";
import { flattenTree, indentLabel } from "@/features/inventory/lib/categoryTree";
import { RangeFilter } from "@/components/ui/RangeFilter";
import type { CreateProductPayload, Product, ProductSortBy, UpdateProductPayload } from "@/features/inventory/types";

const PAGE_SIZE = 50;

const inputStyle: React.CSSProperties = {
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "7px 12px",
  outline: "none",
};

export default function InventoryPage() {
  const t = useTranslations("Dashboard.inventory.page");
  const tCommon = useTranslations("Common");
  const [formOpen, setFormOpen] = useState(false);
  const [editingProduct, setEditingProduct] = useState<Product | null>(null);

  const [search, setSearch] = useState("");
  const [categoryId, setCategoryId] = useState("");
  const [minPrice, setMinPrice] = useState<number | undefined>(undefined);
  const [maxPrice, setMaxPrice] = useState<number | undefined>(undefined);
  const [page, setPage] = useState(1);
  const [sortBy, setSortBy] = useState<ProductSortBy>("name");
  const [sortDescending, setSortDescending] = useState(false);

  // Debounced search (300ms), matching Customers' handleSearchInput pattern — `search` stays
  // the immediately-typed value bound to the input for instant UI feedback, `debouncedSearch`
  // is what actually reaches the query.
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    searchTimerRef.current = setTimeout(() => setDebouncedSearch(search), 300);
    return () => {
      if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    };
  }, [search]);

  // Reset to page 1 whenever a filter/sort changes underneath the current page.
  useEffect(() => {
    setPage(1);
  }, [debouncedSearch, categoryId, minPrice, maxPrice, sortBy, sortDescending]);

  function handleSort(key: ProductSortBy) {
    if (key === sortBy) setSortDescending((d) => !d);
    else {
      // New column defaults to descending, matching the Stock/Receipts/Transfers/WriteOffs
      // precedent (StockTable's handleSort) — only the initial "name" default (set above) is
      // ascending, per the backend's own ItemSortKeys default-key convention.
      setSortBy(key);
      setSortDescending(true);
    }
  }

  const { data, isLoading, isError } = useProductsPaged({
    search: debouncedSearch || undefined,
    category_id: categoryId && categoryId !== "__none__" ? categoryId : undefined,
    uncategorized: categoryId === "__none__" ? true : undefined,
    min_price: minPrice,
    max_price: maxPrice,
    page,
    pageSize: PAGE_SIZE,
    sortBy,
    sortDescending,
  });
  const products = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? Math.ceil(totalCount / PAGE_SIZE);

  const { data: categories = [] } = useCategories();
  const categoryOptions = flattenTree(categories);

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
        toast.success(t("toastCreated"));
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
          toast.success(t("toastUpdated"));
          handleClose();
        },
        onError: (err) => toast.error(err.message),
      },
    );
  };

  const handleDelete = (id: string) => {
    deleteProduct.mutate(id, {
      onSuccess: () => toast.success(t("toastDeleted")),
      onError: (err) => toast.error(err.message),
    });
  };

  if (isError) {
    return (
      <div style={{ padding: "28px 32px", color: "#F87171", fontSize: 13 }}>
        {t("errorLoading")}
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
            {t("title")}
          </h1>
          <p style={{ color: "#4B5563", fontSize: 13, marginTop: 6, marginBottom: 0 }}>
            {isLoading ? tCommon("loading") : t("count", { count: totalCount })}
          </p>
        </div>
        <Btn icon={<Plus size={15} />} onClick={openCreate}>
          {t("addButton")}
        </Btn>
      </div>

      {/* Filters */}
      <div style={{ display: "flex", gap: 10, flexWrap: "wrap", alignItems: "center", marginBottom: 16 }}>
        <input
          type="text"
          placeholder={t("searchPlaceholder")}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          style={{ ...inputStyle, width: 260 }}
        />

        <select
          value={categoryId}
          onChange={(e) => setCategoryId(e.target.value)}
          style={{ ...inputStyle, cursor: "pointer" }}
        >
          <option value="">{t("allCategories")}</option>
          <option value="__none__">{t("uncategorized")}</option>
          {categoryOptions.map(({ category, depth }) => (
            <option key={category.id} value={category.id}>
              {indentLabel(category.name, depth)}
            </option>
          ))}
        </select>

        <RangeFilter
          min={minPrice}
          max={maxPrice}
          onChange={(next) => {
            setMinPrice(next.min);
            setMaxPrice(next.max);
          }}
          placeholder={t("priceRangeLabel")}
        />
      </div>

      {isLoading ? (
        <p style={{ color: "#4B5563", fontSize: 13 }}>{tCommon("loading")}</p>
      ) : (
        <>
          <ProductsTable
            products={products}
            onEdit={openEdit}
            onDelete={handleDelete}
            isDeleting={deleteProduct.isPending}
            sortBy={sortBy}
            sortDescending={sortDescending}
            onSort={handleSort}
          />
          <Pagination page={page} totalPages={totalPages} totalCount={totalCount} onPageChange={setPage} />
        </>
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
