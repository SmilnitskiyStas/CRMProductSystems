"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Eye, Pencil, Trash2, BarChart2, ExternalLink } from "lucide-react";
import { useTranslations, useLocale } from "next-intl";
import type { Product, ProductSortBy } from "../types";
import { ActionMenu } from "@/components/ui/ActionMenu";
import { BarcodeCell } from "./BarcodeCell";
import { Table, type TableColumn } from "@/components/ui/Table";
import {
  DetailDrawer,
  DrawerField,
  DrawerSection,
  DrawerGrid,
} from "@/components/ui/DetailDrawer";
import { ProductAnalyticsTab } from "./ProductAnalyticsTab";

interface Props {
  products: Product[];
  onEdit: (product: Product) => void;
  onDelete: (id: string) => void;
  isDeleting: boolean;
  sortBy: ProductSortBy;
  sortDescending: boolean;
  onSort: (key: ProductSortBy) => void;
}

// ── Perishability class badge ────────────────────────────────────────────────
const PERISHABILITY_BADGE: Record<string, { label: string; bg: string; color: string }> = {
  fresh:    { label: "Fresh",    bg: "#052e16", color: "#4ADE80" },
  chilled:  { label: "Chilled",  bg: "#0c1a3a", color: "#93C5FD" },
  standard: { label: "Standard", bg: "#111827", color: "#9CA3AF" },
  durable:  { label: "Durable",  bg: "#1c1200", color: "#FCD34D" },
};

function PerishabilityBadge({ value }: { value: string }) {
  const badge = PERISHABILITY_BADGE[value] ?? PERISHABILITY_BADGE.standard;
  return (
    <span
      style={{
        display: "inline-block",
        padding: "2px 8px",
        borderRadius: 20,
        background: badge.bg,
        color: badge.color,
        fontSize: 11,
        fontWeight: 600,
      }}
    >
      {badge.label}
    </span>
  );
}

// ── Confirm delete dialog ────────────────────────────────────────────────────
function DeleteDialog({
  product,
  onConfirm,
  onCancel,
  isDeleting,
}: {
  product: Product;
  onConfirm: () => void;
  onCancel: () => void;
  isDeleting: boolean;
}) {
  const t = useTranslations("Dashboard.inventory.table.deleteDialog");
  const tCommon = useTranslations("Common");
  return (
    <>
      <div
        onClick={onCancel}
        style={{
          position: "fixed", inset: 0,
          background: "rgba(0,0,0,0.6)",
          zIndex: 400,
          backdropFilter: "blur(2px)",
        }}
      />
      <div
        style={{
          position: "fixed",
          top: "50%", left: "50%",
          transform: "translate(-50%, -50%)",
          width: "min(420px, 95vw)",
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          zIndex: 401,
          padding: "24px 28px",
        }}
      >
        <h3 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: "0 0 8px" }}>
          {t("title")}
        </h3>
        <p style={{ color: "#6B7280", fontSize: 13, margin: "0 0 20px" }}>
          {t("body", { name: product.name })}
        </p>
        <div style={{ display: "flex", gap: 10, justifyContent: "flex-end" }}>
          <button
            onClick={onCancel}
            style={{
              padding: "8px 16px",
              background: "transparent",
              border: "1px solid #374151",
              borderRadius: 8,
              color: "#9CA3AF",
              fontSize: 13,
              fontWeight: 600,
              cursor: "pointer",
            }}
          >
            {tCommon("cancel")}
          </button>
          <button
            onClick={onConfirm}
            disabled={isDeleting}
            style={{
              padding: "8px 16px",
              background: "#2d0a0a",
              border: "1px solid #7F1D1D",
              borderRadius: 8,
              color: "#F87171",
              fontSize: 13,
              fontWeight: 600,
              cursor: isDeleting ? "not-allowed" : "pointer",
              opacity: isDeleting ? 0.6 : 1,
            }}
          >
            {isDeleting ? t("confirming") : t("confirm")}
          </button>
        </div>
      </div>
    </>
  );
}

// ── Tab bar ──────────────────────────────────────────────────────────────────
type DrawerTab = "info" | "analytics";

function TabBar({ active, onChange }: { active: DrawerTab; onChange: (t: DrawerTab) => void }) {
  const tFields = useTranslations("Dashboard.inventory.fields");
  const tabs: { key: DrawerTab; label: string; icon: React.ReactNode }[] = [
    { key: "info",      label: tFields("tabInfo"), icon: null },
    { key: "analytics", label: tFields("tabAnalytics"),  icon: <BarChart2 size={13} /> },
  ];
  return (
    // Negative margins break out of DetailDrawer's 20px/24px padding
    <div style={{
      display: "flex", gap: 4,
      margin: "-20px -24px 16px",
      padding: "10px 24px",
      borderBottom: "1px solid #1F2937",
      background: "#0D1117",
    }}>
      {tabs.map((t) => (
        <button
          key={t.key}
          onClick={() => onChange(t.key)}
          style={{
            display: "flex", alignItems: "center", gap: 5,
            padding: "5px 12px", borderRadius: 7, fontSize: 12, fontWeight: 600,
            cursor: "pointer",
            background: active === t.key ? "#161B26" : "transparent",
            border: `1px solid ${active === t.key ? "#374151" : "transparent"}`,
            color: active === t.key ? "#E8EDF5" : "#6B7280",
            transition: "all 0.1s",
          }}
        >
          {t.icon}
          {t.label}
        </button>
      ))}
    </div>
  );
}

// ── Detail drawer content ────────────────────────────────────────────────────
function ProductDetail({ p }: { p: Product }) {
  const t = useTranslations("Dashboard.inventory.fields");
  const tItemTypes = useTranslations("Dashboard.inventory.itemTypes");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";

  return (
    <>
      <DrawerSection title={t("sectionMain")}>
        <DrawerField label={t("name")} value={p.name} />
        <DrawerGrid>
          <DrawerField
            label={t("barcode")}
            value={
              p.barcodes && p.barcodes.length > 0 ? (
                <span style={{ fontFamily: "monospace", display: "flex", flexDirection: "column", gap: 2 }}>
                  {p.barcodes.map((b, i) => (
                    <span key={b} style={{ color: i === 0 ? "#E8EDF5" : "#9CA3AF" }}>
                      {i === 0 ? "★ " : ""}
                      {b}
                    </span>
                  ))}
                </span>
              ) : (
                <span style={{ color: "#9CA3AF" }}>—</span>
              )
            }
          />
          <DrawerField label={t("unit")} value={p.unit} />
          <DrawerField label={t("category")} value={p.categoryName ?? "—"} />
          <DrawerField label={t("segment")} value={p.segmentName ?? "—"} />
          <DrawerField label={t("managementType")} value={p.managementType} />
          <DrawerField label={t("itemType")} value={tItemTypes.has(p.itemType) ? tItemTypes(p.itemType) : p.itemType} />
          <DrawerField
            label={t("perishabilityClass")}
            value={<PerishabilityBadge value={p.perishabilityClass ?? "standard"} />}
          />
          <DrawerField
            label={t("statusLabel")}
            value={
              <span
                style={{
                  display: "inline-block",
                  padding: "2px 8px",
                  borderRadius: 20,
                  background: p.isActive ? "#052e16" : "#111827",
                  color: p.isActive ? "#4ADE80" : "#6B7280",
                  fontSize: 11,
                  fontWeight: 600,
                }}
              >
                {p.isActive ? t("statusActive") : t("statusInactive")}
              </span>
            }
          />
        </DrawerGrid>
      </DrawerSection>

      <DrawerSection title={t("sectionPricing")}>
        <DrawerGrid>
          <DrawerField
            label={t("pricePurchase")}
            value={
              p.pricePurchase != null
                ? `${p.pricePurchase.toLocaleString(intlLocale)} ₴`
                : "—"
            }
          />
          <DrawerField
            label={t("priceRetail")}
            value={
              p.priceRetail != null
                ? `${p.priceRetail.toLocaleString(intlLocale)} ₴`
                : "—"
            }
            color="#4ADE80"
          />
          <DrawerField label={t("vatRate")} value={`${p.vatRate}%`} />
          <DrawerField
            label={t("defaultSupplier")}
            value={p.defaultSupplierName ?? "—"}
          />
        </DrawerGrid>
      </DrawerSection>

      <DrawerSection title={t("sectionStock")}>
        <DrawerGrid>
          <DrawerField
            label={t("minStock")}
            value={
              <span style={{ fontFamily: "monospace" }}>{p.minStock}</span>
            }
          />
          <DrawerField
            label={t("maxStock")}
            value={
              <span style={{ fontFamily: "monospace" }}>{p.maxStock}</span>
            }
          />
          <DrawerField
            label={t("safetyBuffer")}
            value={
              <span style={{ fontFamily: "monospace" }}>{p.safetyBuffer}</span>
            }
          />
          <DrawerField
            label={t("shelfLifeDays")}
            value={p.shelfLifeDays != null ? `${p.shelfLifeDays} ${t("shelfLifeDaysSuffix")}` : "—"}
          />
        </DrawerGrid>
      </DrawerSection>

      {(p.storageTempMin != null || p.storageTempMax != null) && (
        <DrawerSection title={t("sectionStorage")}>
          <DrawerGrid>
            <DrawerField
              label={t("storageTempMin")}
              value={p.storageTempMin != null ? `${p.storageTempMin}°C` : "—"}
            />
            <DrawerField
              label={t("storageTempMax")}
              value={p.storageTempMax != null ? `${p.storageTempMax}°C` : "—"}
            />
          </DrawerGrid>
        </DrawerSection>
      )}

      <DrawerSection title={t("sectionSystem")}>
        <DrawerField
          label={t("id")}
          value={
            <span style={{ fontFamily: "monospace", fontSize: 11, color: "#4B5563" }}>
              {p.id}
            </span>
          }
        />
        <DrawerField
          label={t("createdAt")}
          value={new Date(p.createdAt).toLocaleDateString(intlLocale)}
        />
      </DrawerSection>
    </>
  );
}

// ── Table ────────────────────────────────────────────────────────────────────
export function ProductsTable({ products, onEdit, onDelete, isDeleting, sortBy, sortDescending, onSort }: Props) {
  const router = useRouter();
  const t = useTranslations("Dashboard.inventory.table");
  const tFields = useTranslations("Dashboard.inventory.fields");
  const tItemTypes = useTranslations("Dashboard.inventory.itemTypes");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Product | null>(null);
  const [drawerTab, setDrawerTab] = useState<DrawerTab>("info");

  const pendingProduct = products.find((p) => p.id === pendingDeleteId) ?? null;

  function openProduct(product: Product, tab: DrawerTab = "info") {
    setSelected(product);
    setDrawerTab(tab);
  }

  // Column 0 is `name` (not `barcode`) so it gets the product-rule left-align for free from
  // Table's `column.align ?? (index === 0 ? "left" : "center")` default — this reorders the
  // pre-migration baseline's barcode-first layout, the one deliberate visual change called out
  // for this file in the Batch A migration brief.
  const columns: TableColumn<Product>[] = [
    {
      key: "name",
      header: tFields("name"),
      sortKey: "name",
      cellStyle: { color: "#E8EDF5", fontWeight: 500 },
      render: (product) => product.name,
    },
    {
      key: "barcode",
      header: tFields("barcode"),
      sortKey: "barcode",
      cellStyle: { fontFamily: "monospace", fontSize: 12, color: "#4B5563" },
      render: (product) => <BarcodeCell barcodes={product.barcodes} />,
    },
    {
      key: "class",
      header: t("headers.class"),
      render: (product) => <PerishabilityBadge value={product.perishabilityClass ?? "standard"} />,
    },
    {
      key: "category",
      header: tFields("category"),
      sortKey: "category",
      render: (product) => product.categoryName ?? "—",
    },
    {
      key: "itemType",
      header: tFields("itemType"),
      render: (product) =>
        (product.itemType && tItemTypes.has(product.itemType) ? tItemTypes(product.itemType) : product.itemType) ?? "—",
    },
    {
      key: "unit",
      header: tFields("unit"),
      render: (product) => product.unit,
    },
    {
      key: "purchase",
      header: t("headers.purchase"),
      sortKey: "purchaseprice",
      cellStyle: { fontFamily: "monospace" },
      render: (product) =>
        product.pricePurchase != null ? product.pricePurchase.toLocaleString(intlLocale) + " ₴" : "—",
    },
    {
      key: "retail",
      header: t("headers.retail"),
      sortKey: "retailprice",
      cellStyle: { fontFamily: "monospace" },
      render: (product) =>
        product.priceRetail != null ? product.priceRetail.toLocaleString(intlLocale) + " ₴" : "—",
    },
    {
      key: "min",
      header: t("headers.min"),
      sortKey: "minstock",
      cellStyle: { fontFamily: "monospace" },
      render: (product) => product.minStock,
    },
    {
      key: "max",
      header: t("headers.max"),
      sortKey: "maxstock",
      cellStyle: { fontFamily: "monospace" },
      render: (product) => product.maxStock,
    },
    {
      key: "status",
      header: tFields("statusLabel"),
      render: (product) => (
        <span
          style={{
            display: "inline-block",
            padding: "3px 8px",
            borderRadius: 20,
            background: product.isActive ? "#052e16" : "#111827",
            color: product.isActive ? "#4ADE80" : "#6B7280",
            fontSize: 11,
            fontWeight: 600,
          }}
        >
          {product.isActive ? tFields("statusActive") : tFields("statusInactive")}
        </span>
      ),
    },
    {
      key: "actions",
      header: t("headers.actions"),
      render: (product) => (
        <ActionMenu
          items={[
            {
              label: t("actionMenu.view"),
              icon: <Eye size={13} />,
              onClick: () => openProduct(product, "info"),
            },
            {
              label: t("actionMenu.analytics"),
              icon: <BarChart2 size={13} />,
              onClick: () => openProduct(product, "analytics"),
            },
            {
              label: t("actionMenu.openPage"),
              icon: <ExternalLink size={13} />,
              onClick: () => router.push(`/inventory/${product.id}`),
            },
            { separator: true },
            {
              label: t("actionMenu.edit"),
              icon: <Pencil size={13} />,
              onClick: () => onEdit(product),
            },
            {
              label: t("actionMenu.delete"),
              icon: <Trash2 size={13} />,
              variant: "danger",
              onClick: () => setPendingDeleteId(product.id),
            },
          ]}
        />
      ),
    },
  ];

  return (
    <>
      <Table
        columns={columns}
        rows={products}
        rowKey={(product) => product.id}
        sortBy={sortBy}
        sortDescending={sortDescending}
        onSort={onSort}
        minWidth={800}
        emptyMessage={t("empty")}
      />

      {/* Delete confirm */}
      {pendingProduct && (
        <DeleteDialog
          product={pendingProduct}
          isDeleting={isDeleting}
          onConfirm={() => {
            onDelete(pendingProduct.id);
            setPendingDeleteId(null);
          }}
          onCancel={() => setPendingDeleteId(null)}
        />
      )}

      {/* Detail drawer with tabs */}
      <DetailDrawer
        isOpen={selected !== null}
        onClose={() => setSelected(null)}
        title={selected?.name ?? ""}
        subtitle={selected ? `${selected.categoryName ?? tFields("noCategory")} · ${selected.unit}` : ""}
        width={580}
        actions={
          selected && (
            <button
              onClick={() => router.push(`/inventory/${selected.id}`)}
              title={t("openFullPage")}
              style={{
                background: "transparent",
                border: "1px solid #1F2937",
                borderRadius: 7,
                color: "#6B7280",
                padding: "4px 8px",
                cursor: "pointer",
                display: "flex",
                alignItems: "center",
                transition: "color 0.1s, border-color 0.1s",
              }}
              onMouseEnter={(e) => {
                (e.currentTarget as HTMLElement).style.color = "#9CA3AF";
                (e.currentTarget as HTMLElement).style.borderColor = "#374151";
              }}
              onMouseLeave={(e) => {
                (e.currentTarget as HTMLElement).style.color = "#6B7280";
                (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
              }}
            >
              <ExternalLink size={14} />
            </button>
          )
        }
      >
        {selected && (
          <>
            <TabBar active={drawerTab} onChange={setDrawerTab} />
            {drawerTab === "info"      && <ProductDetail p={selected} />}
            {drawerTab === "analytics" && (
              <ProductAnalyticsTab
                productId={selected.id}
                buffers={{
                  safetyBuffer: selected.safetyBuffer,
                  minStock: selected.minStock,
                  maxStock: selected.maxStock,
                }}
              />
            )}
          </>
        )}
      </DetailDrawer>
    </>
  );
}
