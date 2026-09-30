"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { ApiError } from "@/lib/api";
import { ProductSearchPicker } from "@/features/inventory/components/ProductSearchPicker";
import { productsApi } from "@/features/inventory/api/products";
import { useProduct, useProducts } from "@/features/inventory/hooks/useProducts";
import { useStock } from "@/features/shelf/hooks/useStock";
import { StatusBadge } from "@/features/shelf/components/StatusBadge";
import { useProductSalesTrend } from "@/features/analytics/hooks/usePosAnalytics";
import type { Product } from "@/features/inventory/types";

interface Props {
  onClose: () => void;
  storeId: string;
  zoneId: string;
  sectionLabel: string;
  currentProductId: string | null;
  onLink: (product: Product) => void;
  onUnlink: () => void;
}

/**
 * Section detail/pick modal for the shelf-builder canvas. Replaces the old inline
 * "expand a search box in the Sections list" flow — clicking a canvas box or a Sections-list
 * row now opens this instead, so picking a product comes with the same rich context (stock,
 * expiry, recent sales, promo, similar products) whether you're looking at what's already
 * there or comparing a candidate before committing.
 *
 * Three steps, `list`/`preview` shared by both "linking a fresh section" and "changing an
 * existing one":
 *   view    — the section's currently linked product's detail (skipped if nothing linked yet)
 *   list    — ProductSearchPicker
 *   preview — the searched candidate's detail, with a confirm/back pair
 */
export function ShelfProductModal({ onClose, storeId, zoneId, sectionLabel, currentProductId, onLink, onUnlink }: Props) {
  const t = useTranslations("Dashboard.locations.shelvesPage");
  const queryClient = useQueryClient();

  const [step, setStep] = useState<"view" | "list" | "preview">(currentProductId ? "view" : "list");
  const [activeProductId, setActiveProductId] = useState(currentProductId);
  const [previewProduct, setPreviewProduct] = useState<Product | null>(null);

  const { data: activeProduct } = useProduct(activeProductId ?? "");

  function handlePick(product: Product) {
    setPreviewProduct(product);
    setStep("preview");
  }

  // Confirming here persists the item_zone_assignments tag immediately (productsApi.assignZone)
  // rather than waiting for the page's own "Save" — that button only ever persisted the shelf
  // *layout* JSON, and shelves/page.tsx's syncZoneTags ran afterward as a batch. A merchandiser
  // who links a product here reasonably expects "this product is now in this zone" to already be
  // true elsewhere (catalog Zones column, the product's own Zones section) even if they never
  // click that unrelated Save button. A 400 (already tagged — e.g. this exact zone was assigned
  // on a previous save) is expected and not surfaced as an error.
  async function confirmLink() {
    if (!previewProduct) return;
    const product = previewProduct;
    onLink(product);
    setActiveProductId(product.id);
    setPreviewProduct(null);
    setStep("view");

    try {
      await productsApi.assignZone(product.id, zoneId);
      toast.success(t("productLinkedToast"));
    } catch (err) {
      if (!(err instanceof ApiError && err.status === 400)) {
        toast.error(err instanceof Error ? err.message : String(err));
        return;
      }
    }
    queryClient.invalidateQueries({ queryKey: ["products", product.id, "zones"] });
    queryClient.invalidateQueries({ queryKey: ["products"] });
  }

  function handleUnlink() {
    onUnlink();
    onClose();
  }

  return (
    <Modal title={t("modalTitle", { label: sectionLabel })} onClose={onClose} width={640}>
      {step === "view" && activeProduct && (
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <ProductInfoPanel product={activeProduct} storeId={storeId} />
          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
            <Btn variant="danger" onClick={handleUnlink}>
              {t("unlinkProduct")}
            </Btn>
            <Btn variant="ghost" onClick={() => setStep("list")}>
              {t("addProduct")}
            </Btn>
          </div>
        </div>
      )}

      {step === "list" && (
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <ProductSearchPicker excludeIds={[]} onPick={handlePick} />
          {currentProductId && (
            <div style={{ display: "flex", justifyContent: "flex-end" }}>
              <Btn variant="ghost" onClick={() => setStep("view")}>
                {t("backToProduct")}
              </Btn>
            </div>
          )}
        </div>
      )}

      {step === "preview" && previewProduct && (
        <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <ProductInfoPanel product={previewProduct} storeId={storeId} />
          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end" }}>
            <Btn variant="ghost" onClick={() => { setPreviewProduct(null); setStep("list"); }}>
              {t("backToSearch")}
            </Btn>
            <Btn onClick={confirmLink}>{t("confirmLink")}</Btn>
          </div>
        </div>
      )}
    </Modal>
  );
}

// ── Product detail panel — stock/expiry, popularity, promo, similar products ─────────────────
// Shared by the "view current" and "preview candidate" steps above so the same information
// backs both a look-back and a decision.

function formatExpiryDate(dateStr: string): string {
  const [y, m, d] = dateStr.split("-");
  return `${d}.${m}.${y}`;
}

function ProductInfoPanel({ product, storeId }: { product: Product; storeId: string }) {
  const t = useTranslations("Dashboard.locations.shelvesPage");
  const tStock = useTranslations("Dashboard.shelf.stockTable");
  const tPromo = useTranslations("Dashboard.inventory.table");
  const tFields = useTranslations("Dashboard.inventory.fields");

  // Default sortBy (StockSortKeys.Default = "expirydate") is already FEFO order — nearest
  // expiry first — exactly what this batch list should read as.
  const { data: stockPage, isLoading: stockLoading } = useStock(
    { store_id: storeId, product_id: product.id, pageSize: 50 },
  );
  const batches = stockPage?.items ?? [];
  const totalQty = batches.reduce((sum, b) => sum + b.quantity, 0);

  const to = new Date().toISOString().slice(0, 10);
  const from = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);
  const { data: trend } = useProductSalesTrend(product.id, { from, to, store_id: storeId });
  const sold30d = (trend?.points ?? []).reduce((sum, p) => sum + p.quantity, 0);

  const { data: sameCategory = [] } = useProducts(
    { category_id: product.categoryId ?? undefined, pageSize: 6 },
    !!product.categoryId,
  );
  const similar = sameCategory.filter((p) => p.id !== product.id).slice(0, 5);

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      {/* Header: name, unit, barcode, promo */}
      <div>
        <div style={{ display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap" }}>
          <span style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700 }}>{product.name}</span>
          {product.promoState === "active" && (
            <span style={{ ...promoBadgeStyle, background: "#0F2D1A", border: "1px solid #166534", color: "#4ADE80" }}>
              🏷 {tPromo("promoActivePct", { pct: Math.round(product.promoDiscountPercent ?? 0) })}
            </span>
          )}
          {product.promoState === "upcoming" && (
            <span style={{ ...promoBadgeStyle, background: "#2A2000", border: "1px solid #854D0E", color: "#FCD34D" }}>
              🏷 {tPromo("promoUpcoming")}
            </span>
          )}
        </div>
        <div style={{ color: "#6B7280", fontSize: 12, marginTop: 2 }}>
          {product.categoryName ?? tFields("noCategory")} · {product.unit}
          {product.barcodes[0] && ` · ${product.barcodes[0]}`}
          {product.priceRetail != null && ` · ${product.priceRetail.toLocaleString("uk-UA")} ₴`}
        </div>
      </div>

      {/* Stock + expiry batches */}
      <div>
        <div style={{ display: "flex", alignItems: "baseline", justifyContent: "space-between", marginBottom: 6 }}>
          <span style={sectionTitleStyle}>{t("infoStockTitle")}</span>
          <span style={{ color: "#9CA3AF", fontSize: 12 }}>
            {t("infoTotalQuantity", { qty: totalQty, unit: product.unit })}
          </span>
        </div>
        {stockLoading ? (
          <p style={hintTextStyle}>{tStock("loading")}</p>
        ) : batches.length === 0 ? (
          <p style={hintTextStyle}>{t("infoNoStock")}</p>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 4, maxHeight: 160, overflowY: "auto" }}>
            {batches.map((b) => (
              <div
                key={b.id}
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: 8,
                  padding: "6px 10px",
                  background: "#0B0E14",
                  border: "1px solid #1F2937",
                  borderRadius: 6,
                  fontSize: 12,
                }}
              >
                <span style={{ color: "#9CA3AF" }}>{b.zoneName ?? tStock("headers.zone") + " —"}</span>
                <span style={{ color: "#E8EDF5", fontFamily: "monospace" }}>{formatExpiryDate(b.expiryDate)}</span>
                <span style={{ color: "#E8EDF5", fontFamily: "monospace" }}>{b.quantity} {product.unit}</span>
                <StatusBadge status={b.status} />
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Popularity */}
      <div>
        <span style={sectionTitleStyle}>{t("infoPopularityTitle")}</span>
        <p style={{ color: "#9CA3AF", fontSize: 12, margin: "6px 0 0" }}>
          {t("infoSoldLast30Days", { qty: sold30d, unit: product.unit })}
        </p>
      </div>

      {/* Similar products in the same category */}
      {product.categoryId && (
        <div>
          <span style={sectionTitleStyle}>{t("infoSimilarTitle")}</span>
          {similar.length === 0 ? (
            <p style={hintTextStyle}>{t("infoNoSimilar")}</p>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: 4, marginTop: 6 }}>
              {similar.map((p) => (
                <div
                  key={p.id}
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    gap: 8,
                    fontSize: 12,
                    color: "#9CA3AF",
                  }}
                >
                  <span style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>{p.name}</span>
                  {p.priceRetail != null && (
                    <span style={{ flexShrink: 0, fontFamily: "monospace" }}>
                      {p.priceRetail.toLocaleString("uk-UA")} ₴
                    </span>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}

const sectionTitleStyle: React.CSSProperties = {
  color: "#4B5563",
  fontSize: 11,
  fontWeight: 600,
  textTransform: "uppercase",
  letterSpacing: "0.05em",
};

const hintTextStyle: React.CSSProperties = { color: "#4B5563", fontSize: 12, margin: "4px 0 0" };

const promoBadgeStyle: React.CSSProperties = {
  display: "inline-block",
  padding: "1px 7px",
  borderRadius: 20,
  fontSize: 10,
  fontWeight: 700,
  whiteSpace: "nowrap",
};
