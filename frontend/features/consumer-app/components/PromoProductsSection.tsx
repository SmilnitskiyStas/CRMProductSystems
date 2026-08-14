"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Btn } from "@/components/ui/Btn";
import { Switch } from "@/components/ui/switch";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useCatalogProducts } from "@/features/catalog/hooks/useCatalog";
import {
  useCancelPromoProduct,
  useCreatePromoProduct,
  usePromoProducts,
  usePublishPromoProduct,
} from "../hooks/usePromoProducts";
import { LifecycleTabs, type LifecycleTab } from "./LifecycleTabs";
import type { DiscountStatus } from "../types";

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 24,
};

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#0D1117",
  border: "1px solid #374151",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};

const labelStyle: React.CSSProperties = {
  display: "block",
  color: "#9CA3AF",
  fontSize: 12,
  fontWeight: 500,
  marginBottom: 6,
};

const selectStyle: React.CSSProperties = { ...inputStyle, cursor: "pointer" };

const rowStyle: React.CSSProperties = {
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "10px 14px",
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  gap: 12,
};

/** DiscountStatus → LifecycleTab bucket (TASK-525). pending = draft (never approved),
 * active = running, expired|cancelled = past — mirrors BannerLifecycleStatus's 3-bucket shape
 * so both sections share the same LifecycleTabs component. */
function tabOf(status: DiscountStatus): LifecycleTab {
  if (status === "pending") return "draft";
  if (status === "active") return "running";
  return "past";
}

/**
 * TASK-522: "promo products" admin section. Reuses the pre-existing DiscountsController as-is
 * (reason="promo") since no new backend exists for this per the plan. Store-scoped: pick a
 * store, see/manage its promo discounts.
 *
 * TASK-525: added the Активні/Минулі/Чернетки history tabs (bucketed from `status` — GET
 * /api/discounts?storeId, no `status` filter, so one fetch covers the whole history) plus the
 * "Опублікувати одразу" create-time toggle and a Чернетки-tab "Опублікувати" row action.
 */
export function PromoProductsSection() {
  const t = useTranslations("Dashboard.consumerApp.promoProducts");
  const { data: locations } = useLocations();
  const activeLocations = useMemo(() => (locations ?? []).filter((l) => l.isActive), [locations]);
  const { data: catalogProducts } = useCatalogProducts();
  const productById = useMemo(() => {
    const map = new Map<string, { name: string; unit: string }>();
    for (const p of catalogProducts ?? []) map.set(p.id, { name: p.name, unit: p.unit });
    return map;
  }, [catalogProducts]);

  const [storeId, setStoreId] = useState<string>("");
  const effectiveStoreId = storeId || activeLocations[0]?.id || "";

  const { data: promos, isLoading, isError } = usePromoProducts(effectiveStoreId || null);
  const create = useCreatePromoProduct();
  const cancel = useCancelPromoProduct();
  const publish = usePublishPromoProduct();

  const [tab, setTab] = useState<LifecycleTab>("running");
  const counts = useMemo(() => {
    const c: Partial<Record<LifecycleTab, number>> = { running: 0, past: 0, draft: 0 };
    for (const p of promos ?? []) {
      const key = tabOf(p.status);
      c[key] = (c[key] ?? 0) + 1;
    }
    return c;
  }, [promos]);
  const visiblePromos = useMemo(
    () => (promos ?? []).filter((p) => tabOf(p.status) === tab),
    [promos, tab],
  );

  const [addOpen, setAddOpen] = useState(false);
  const [productId, setProductId] = useState("");
  const [discountPercent, setDiscountPercent] = useState("10");
  const [validUntil, setValidUntil] = useState("");
  const [publishImmediately, setPublishImmediately] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const activeCatalogProducts = useMemo(
    () => (catalogProducts ?? []).filter((p) => p.isActive),
    [catalogProducts],
  );

  async function handleAdd(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!productId || !effectiveStoreId) {
      setError(t("productRequiredError"));
      return;
    }
    const pct = Number(discountPercent);
    if (!Number.isFinite(pct) || pct <= 0 || pct > 100) {
      setError(t("discountRangeError"));
      return;
    }
    const product = activeCatalogProducts.find((p) => p.id === productId);
    try {
      await create.mutateAsync({
        productId,
        storeId: effectiveStoreId,
        discountPercent: pct,
        reason: "promo",
        priceOriginal: product?.priceRetail ?? undefined,
        // Discount.ValidUntil is `timestamp with time zone` — a bare "YYYY-MM-DD" from the date
        // input parses as DateTime.Kind=Unspecified, which Npgsql rejects (same issue fixed in
        // BannerForm's toUtcIso). Pin to UTC midnight.
        validUntil: validUntil ? `${validUntil}T00:00:00.000Z` : undefined,
        publishImmediately,
      });
      toast.success(publishImmediately ? t("addSuccess") : t("addDraftSuccess"));
      setAddOpen(false);
      setProductId("");
      setDiscountPercent("10");
      setValidUntil("");
      setPublishImmediately(true);
      if (publishImmediately) setTab("running");
      else setTab("draft");
    } catch (err) {
      setError(err instanceof Error ? err.message : t("addError"));
    }
  }

  async function handleCancel(id: string) {
    try {
      await cancel.mutateAsync(id);
      toast.success(t("cancelSuccess"));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t("cancelError"));
    }
  }

  async function handlePublish(id: string) {
    try {
      await publish.mutateAsync(id);
      toast.success(t("publishSuccess"));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t("publishError"));
    }
  }

  return (
    <div style={cardStyle}>
      <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20, flexWrap: "wrap", gap: 12 }}>
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <span style={{ fontSize: 24 }}>🏷️</span>
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>{t("title")}</h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: 0, marginTop: 3 }}>{t("subtitle")}</p>
          </div>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
          <select
            value={effectiveStoreId}
            onChange={(e) => setStoreId(e.target.value)}
            style={{ ...selectStyle, width: 220 }}
          >
            {activeLocations.length === 0 && <option value="">{t("noStores")}</option>}
            {activeLocations.map((loc) => (
              <option key={loc.id} value={loc.id}>{loc.name}</option>
            ))}
          </select>
          <Btn size="sm" onClick={() => setAddOpen((v) => !v)} disabled={!effectiveStoreId}>
            {t("addButton")}
          </Btn>
        </div>
      </div>

      {addOpen && (
        <form
          onSubmit={handleAdd}
          style={{
            display: "grid", gridTemplateColumns: "2fr 1fr 1fr auto", gap: 10, alignItems: "end",
            marginBottom: 18, padding: 14, background: "#111827", border: "1px solid #1F2937", borderRadius: 10,
          }}
        >
          <div>
            <label style={labelStyle}>{t("productLabel")}</label>
            <select value={productId} onChange={(e) => setProductId(e.target.value)} style={selectStyle}>
              <option value="">{t("productPlaceholder")}</option>
              {activeCatalogProducts.map((p) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
            </select>
          </div>
          <div>
            <label style={labelStyle}>{t("discountPercentLabel")}</label>
            <input
              // min must be 0 (not 0.01) — with step=0.1, a min of 0.01 makes the default "10"
              // fail HTML5 step-mismatch validation, which silently blocks native form
              // submission (no submit event, no visible error). Server still rejects <=0 (see
              // DiscountService.CreateAsync), so 0 as a min here is just for step alignment.
              type="number" min={0} max={100} step="0.1"
              value={discountPercent}
              onChange={(e) => setDiscountPercent(e.target.value)}
              style={inputStyle}
            />
          </div>
          <div>
            <label style={labelStyle}>{t("validUntilLabel")}</label>
            <input type="date" value={validUntil} onChange={(e) => setValidUntil(e.target.value)} style={inputStyle} />
          </div>
          <Btn type="submit" size="sm" disabled={create.isPending}>
            {create.isPending ? t("addingButton") : t("confirmAddButton")}
          </Btn>
          <div style={{ gridColumn: "1 / -1", display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
            <div>
              <div style={{ color: "#E8EDF5", fontSize: 12, fontWeight: 500 }}>{t("publishImmediatelyLabel")}</div>
              <div style={{ color: "#4B5563", fontSize: 11, marginTop: 2 }}>{t("publishImmediatelyHint")}</div>
            </div>
            <Switch checked={publishImmediately} onCheckedChange={setPublishImmediately} />
          </div>
          {error && <p style={{ color: "#F87171", fontSize: 11, gridColumn: "1 / -1", margin: 0 }}>{error}</p>}
        </form>
      )}

      <LifecycleTabs
        tab={tab}
        onTabChange={setTab}
        counts={counts}
        labels={{ running: t("tabRunning"), past: t("tabPast"), draft: t("tabDraft") }}
      />

      {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}
      {isError && <div style={{ color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>}
      {!isLoading && !isError && visiblePromos.length === 0 && (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("emptyHint")}</div>
      )}

      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {visiblePromos.map((promo) => {
          const product = productById.get(promo.productId);
          const isDraft = promo.status === "pending";
          const isRunning = promo.status === "active";
          return (
            <div key={promo.id} style={rowStyle}>
              <div style={{ minWidth: 0 }}>
                <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                  {product?.name ?? promo.productId}
                </div>
                <div style={{ color: "#6B7280", fontSize: 12, marginTop: 2, display: "flex", gap: 12, flexWrap: "wrap" }}>
                  <span>{t("discountBadge", { percent: promo.discountPercent })}</span>
                  {promo.priceOriginal != null && promo.priceDiscounted != null && (
                    <span>
                      <span style={{ textDecoration: "line-through", opacity: 0.6 }}>{promo.priceOriginal.toFixed(2)}</span>
                      {" → "}
                      <span style={{ color: "#4ADE80" }}>{promo.priceDiscounted.toFixed(2)}</span>
                    </span>
                  )}
                  <span>{t("validUntilShort", { date: promo.validUntil ? new Date(promo.validUntil).toLocaleDateString() : t("noExpiry") })}</span>
                </div>
              </div>
              {/* Минулі (expired/cancelled) is read-only — Discount.cs's own guards refuse
                  approve/cancel outside pending/active. */}
              {(isDraft || isRunning) && (
                <div style={{ display: "flex", gap: 8, flexShrink: 0 }}>
                  {isDraft && (
                    <Btn size="sm" variant="success" onClick={() => handlePublish(promo.id)} disabled={publish.isPending}>
                      {t("publishButton")}
                    </Btn>
                  )}
                  <Btn size="sm" variant="danger" onClick={() => handleCancel(promo.id)} disabled={cancel.isPending}>
                    {t("removeButton")}
                  </Btn>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
