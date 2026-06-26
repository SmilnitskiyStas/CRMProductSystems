"use client";

import { useState } from "react";
import { useParams, useRouter, useSearchParams } from "next/navigation";
import {
  ArrowLeft, BarChart2, Info, ExternalLink,
  ArrowDownToLine, TrendingUp, Trash2, RefreshCw, Loader2,
} from "lucide-react";
import { useProduct } from "@/features/inventory/hooks/useProducts";
import { ProductAnalyticsTab } from "@/features/inventory/components/ProductAnalyticsTab";
import { ITEM_TYPE_LABELS } from "@/features/inventory/components/ProductForm";

// ── Tab ───────────────────────────────────────────────────────────────────────

type PageTab = "info" | "analytics";

function TabBar({ active, onChange }: { active: PageTab; onChange: (t: PageTab) => void }) {
  const tabs: { key: PageTab; label: string; icon: React.ReactNode }[] = [
    { key: "info",      label: "Інформація", icon: <Info size={14} /> },
    { key: "analytics", label: "Аналітика",  icon: <BarChart2 size={14} /> },
  ];
  return (
    <div style={{ display: "flex", gap: 4 }}>
      {tabs.map((t) => (
        <button
          key={t.key}
          onClick={() => onChange(t.key)}
          style={{
            display: "flex", alignItems: "center", gap: 6,
            padding: "7px 16px", borderRadius: 8, fontSize: 13, fontWeight: 600,
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

// ── Info section ──────────────────────────────────────────────────────────────

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{
      background: "#0D1117", border: "1px solid #1F2937",
      borderRadius: 12, padding: "18px 20px",
    }}>
      <div style={{
        color: "#4B5563", fontSize: 11, fontWeight: 600,
        textTransform: "uppercase", letterSpacing: "0.05em",
        marginBottom: 14, paddingBottom: 10,
        borderBottom: "1px solid #1F2937",
      }}>
        {title}
      </div>
      {children}
    </div>
  );
}

function Field({ label, value, mono }: { label: string; value: React.ReactNode; mono?: boolean }) {
  return (
    <div>
      <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 3 }}>{label}</div>
      <div style={{
        color: "#E8EDF5", fontSize: 13, fontWeight: 500,
        fontFamily: mono ? "monospace" : undefined,
      }}>
        {value ?? "—"}
      </div>
    </div>
  );
}

function Grid({ children }: { children: React.ReactNode }) {
  return (
    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "14px 24px" }}>
      {children}
    </div>
  );
}

function StatCard({
  icon, label, value, color,
}: {
  icon: React.ReactNode; label: string; value: React.ReactNode; color: string;
}) {
  return (
    <div style={{
      background: "#0D1117", border: "1px solid #1F2937",
      borderRadius: 10, padding: "14px 18px",
      display: "flex", alignItems: "center", gap: 12,
    }}>
      <div style={{
        width: 36, height: 36, borderRadius: 9, flexShrink: 0,
        background: `${color}15`, border: `1px solid ${color}30`,
        display: "flex", alignItems: "center", justifyContent: "center",
      }}>
        {icon}
      </div>
      <div>
        <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em" }}>
          {label}
        </div>
        <div style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, fontFamily: "monospace", marginTop: 2 }}>
          {value}
        </div>
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function ProductPage() {
  const { id } = useParams<{ id: string }>();
  const router       = useRouter();
  const searchParams = useSearchParams();
  const [tab, setTab] = useState<PageTab>(
    searchParams.get("tab") === "analytics" ? "analytics" : "info",
  );

  const { data: product, isLoading } = useProduct(id);

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
        Товар не знайдено
      </div>
    );
  }

  return (
    <div style={{ padding: "24px 32px", display: "flex", flexDirection: "column", gap: 20 }}>

      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
        <button
          onClick={() => router.push("/inventory")}
          style={{
            background: "transparent", border: "none",
            color: "#6B7280", cursor: "pointer",
            display: "flex", alignItems: "center", gap: 4,
            fontSize: 13, padding: 0, flexShrink: 0,
          }}
        >
          <ArrowLeft size={15} /> Каталог
        </button>

        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 10, flexWrap: "wrap" }}>
            <h1 style={{ color: "#E8EDF5", fontSize: 20, fontWeight: 700, margin: 0 }}>
              {product.name}
            </h1>
            <span style={{
              display: "inline-block", padding: "2px 8px", borderRadius: 20,
              background: product.isActive ? "#052e16" : "#111827",
              color: product.isActive ? "#4ADE80" : "#6B7280",
              fontSize: 11, fontWeight: 600,
            }}>
              {product.isActive ? "Активний" : "Неактивний"}
            </span>
          </div>
          <p style={{ color: "#4B5563", fontSize: 12, marginTop: 3, marginBottom: 0 }}>
            {product.categoryName ?? "Без категорії"}
            {product.barcode && ` · ${product.barcode}`}
            {` · ${product.unit}`}
          </p>
        </div>
      </div>

      {/* Tabs */}
      <div style={{
        borderBottom: "1px solid #1F2937",
        paddingBottom: 0,
      }}>
        <TabBar active={tab} onChange={setTab} />
      </div>

      {/* ── Info tab ─────────────────────────────────────────────────────── */}
      {tab === "info" && (
        <div style={{ maxWidth: 1100, display: "flex", flexDirection: "column", gap: 20 }}>
          {/* Summary cards */}
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))", gap: 12 }}>
            <StatCard
              icon={<ArrowDownToLine size={16} color="#3B82F6" />}
              label="Мін. залишок"
              value={product.minStock}
              color="#3B82F6"
            />
            <StatCard
              icon={<TrendingUp size={16} color="#A78BFA" />}
              label="Макс. залишок"
              value={product.maxStock}
              color="#A78BFA"
            />
            <StatCard
              icon={<RefreshCw size={16} color="#4ADE80" />}
              label="Буфер безпеки"
              value={product.safetyBuffer}
              color="#4ADE80"
            />
            {product.shelfLifeDays != null && (
              <StatCard
                icon={<Trash2 size={16} color="#F87171" />}
                label="Термін зберігання"
                value={`${product.shelfLifeDays} дн.`}
                color="#F87171"
              />
            )}
          </div>

          {/* Sections grid */}
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 16 }}>

            {/* Main info */}
            <Section title="Основна інформація">
              <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
                <Field label="Назва" value={product.name} />
                <Grid>
                  <Field label="Штрихкод" value={<span style={{ fontFamily: "monospace", color: "#9CA3AF" }}>{product.barcode ?? "—"}</span>} />
                  <Field label="Одиниця виміру" value={product.unit} />
                  <Field label="Категорія" value={product.categoryName ?? "—"} />
                  <Field label="Сегмент" value={product.segmentName ?? "—"} />
                  <Field label="Тип управління" value={product.managementType} />
                  <Field label="Тип товару" value={ITEM_TYPE_LABELS[product.itemType] ?? product.itemType} />
                </Grid>
              </div>
            </Section>

            {/* Prices */}
            <Section title="Ціни та ПДВ">
              <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
                <Grid>
                  <div>
                    <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 3 }}>Закупівельна ціна</div>
                    <div style={{ color: "#E8EDF5", fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
                      {product.pricePurchase != null ? `${product.pricePurchase.toLocaleString("uk-UA")} ₴` : "—"}
                    </div>
                  </div>
                  <div>
                    <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 3 }}>Роздрібна ціна</div>
                    <div style={{ color: "#4ADE80", fontSize: 18, fontWeight: 700, fontFamily: "monospace" }}>
                      {product.priceRetail != null ? `${product.priceRetail.toLocaleString("uk-UA")} ₴` : "—"}
                    </div>
                  </div>
                </Grid>
                <Grid>
                  <Field label="Ставка ПДВ" value={`${product.vatRate}%`} />
                  <Field label="Постачальник за замовч." value={product.defaultSupplierName ?? "—"} />
                </Grid>
              </div>
            </Section>

            {/* Stock */}
            <Section title="Залишки та буфери">
              <Grid>
                <Field label="Мін. залишок" value={<span style={{ fontFamily: "monospace" }}>{product.minStock}</span>} />
                <Field label="Макс. залишок" value={<span style={{ fontFamily: "monospace" }}>{product.maxStock}</span>} />
                <Field label="Буфер безпеки" value={<span style={{ fontFamily: "monospace" }}>{product.safetyBuffer}</span>} />
                <Field label="Термін зберігання" value={product.shelfLifeDays != null ? `${product.shelfLifeDays} дн.` : "—"} />
              </Grid>
            </Section>

            {/* Storage conditions */}
            {(product.storageTempMin != null || product.storageTempMax != null) && (
              <Section title="Умови зберігання">
                <Grid>
                  <Field label="Темп. мін." value={product.storageTempMin != null ? `${product.storageTempMin}°C` : "—"} />
                  <Field label="Темп. макс." value={product.storageTempMax != null ? `${product.storageTempMax}°C` : "—"} />
                </Grid>
              </Section>
            )}

            {/* System */}
            <Section title="Системна інформація">
              <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
                <Field
                  label="ID"
                  value={<span style={{ fontFamily: "monospace", fontSize: 11, color: "#4B5563" }}>{product.id}</span>}
                />
                <Field
                  label="Дата створення"
                  value={new Date(product.createdAt).toLocaleDateString("uk-UA")}
                />
              </div>
            </Section>
          </div>
        </div>
      )}

      {/* ── Analytics tab ──────────────────────────────────────────────── */}
      {tab === "analytics" && (
        <ProductAnalyticsTab
          productId={id}
          chartHeight={380}
          buffers={{
            safetyBuffer: product.safetyBuffer,
            minStock: product.minStock,
            maxStock: product.maxStock,
          }}
        />
      )}
    </div>
  );
}
