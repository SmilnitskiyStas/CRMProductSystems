"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { Btn } from "@/components/ui/Btn";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 24,
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  gap: 20,
  flexWrap: "wrap",
};

/**
 * TASK-522: minimal read-only status card, per the plan — the catalog already has full CRUD
 * at /catalog, so this section deliberately does not duplicate it. It just confirms the
 * catalog is automatically available to consumer-app shoppers and links to the real page.
 */
export function CatalogSection() {
  const t = useTranslations("Dashboard.consumerApp.catalog");
  const { data: products, isLoading } = useCatalogProducts();
  const activeCount = (products ?? []).filter((p) => p.isActive).length;

  return (
    <div style={cardStyle}>
      <div style={{ display: "flex", alignItems: "center", gap: 16 }}>
        <span style={{ fontSize: 24 }}>📖</span>
        <div>
          <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
          <p style={{ color: "#4B5563", fontSize: 12, margin: 0, marginTop: 3, maxWidth: 480 }}>
            {t("description")}
          </p>
          <p style={{ color: "#9CA3AF", fontSize: 12, margin: 0, marginTop: 6 }}>
            {isLoading ? t("loading") : t("activeCount", { count: activeCount })}
          </p>
        </div>
      </div>
      {/* No standalone /catalog route exists — the product catalog is managed on /inventory
          (features/inventory's ProductForm/ProductsTable); link there instead of the plan's
          literal /catalog path. */}
      <Link href="/inventory" style={{ textDecoration: "none" }}>
        <Btn variant="ghost">{t("openButton")}</Btn>
      </Link>
    </div>
  );
}
