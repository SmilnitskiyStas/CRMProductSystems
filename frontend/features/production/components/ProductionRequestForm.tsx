"use client";

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Trash2 } from "lucide-react";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { useLocations } from "@/features/locations/hooks/useLocations";
import { useStock } from "@/features/shelf/hooks/useStock";
import { useProductSearch } from "@/features/inventory/hooks/useProducts";
import {
  useCreateProductionRequest,
  useUpdateProductionRequest,
} from "../hooks/useProductionRequests";
import type { ProductionRequestDetailDto } from "../types";
import { reqInputStyle, reqLabelStyle, reqErrorStyle, errMessage } from "./ProductionRequestStatusBadge";

interface Line {
  itemId: string;
  itemName: string;
  unit: string;
  qty: string;
}

interface Props {
  /** When set, edits this draft instead of creating a new request. */
  draft?: ProductionRequestDetailDto;
  onClose: () => void;
}

/** Current on-hand stock of one item at the selected store (sums batches, shows nearest expiry). */
function StockHint({ storeId, itemId, unit }: { storeId: string; itemId: string; unit: string }) {
  const t = useTranslations("Dashboard.production.requests.form");
  const { data, isLoading } = useStock({ store_id: storeId, product_id: itemId, pageSize: 100 }, !!storeId);
  const base = { fontSize: 11, marginTop: 2 } as const;
  if (!storeId) return <div style={{ ...base, color: "#4B5563" }}>{t("stockNeedStore")}</div>;
  if (isLoading) return <div style={{ ...base, color: "#4B5563" }}>…</div>;
  const batches = (data?.items ?? []).filter((b) => b.quantity > 0);
  const total = batches.reduce((s, b) => s + b.quantity, 0);
  if (total <= 0) return <div style={{ ...base, color: "#F87171" }}>{t("noStock")}</div>;
  const nearest = batches.map((b) => b.expiryDate).filter(Boolean).sort()[0];
  return (
    <div style={{ ...base, color: "#34D399" }}>
      {t("stockOnHand")}: {Math.round(total * 1000) / 1000} {unit}
      {nearest ? ` · ${t("stockNearestExpiry")}: ${nearest}` : ""}
    </div>
  );
}

export function ProductionRequestForm({ draft, onClose }: Props) {
  const t = useTranslations("Dashboard.production.requests.form");
  const { data: locations = [] } = useLocations();
  const create = useCreateProductionRequest();
  const update = useUpdateProductionRequest(draft?.id ?? "");

  const [storeId, setStoreId] = useState(draft?.storeLocationId ?? "");
  const [prodId, setProdId] = useState(draft?.productionLocationId ?? "");
  const [neededBy, setNeededBy] = useState(draft?.neededBy ?? "");
  const [notes, setNotes] = useState(draft?.notes ?? "");
  const [lines, setLines] = useState<Line[]>(
    draft?.lines.map((l) => ({
      itemId: l.itemId,
      itemName: l.itemName,
      unit: l.unit,
      qty: String(l.requestedQty),
    })) ?? []
  );
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const h = setTimeout(() => setDebounced(search), 250);
    return () => clearTimeout(h);
  }, [search]);
  const { data: found = [] } = useProductSearch(debounced);

  const stores = locations.filter((l) => l.locationType !== "production" && l.isActive);
  const prods = locations.filter((l) => l.locationType === "production" && l.isActive);
  // Convenience: a single production location is preselected.
  useEffect(() => {
    if (!draft && !prodId && prods.length === 1) setProdId(prods[0].id);
  }, [draft, prodId, prods]);

  function addItem(p: { id: string; name: string; unit: string }) {
    setSearch("");
    setLines((prev) =>
      prev.some((l) => l.itemId === p.id)
        ? prev
        : [...prev, { itemId: p.id, itemName: p.name, unit: p.unit, qty: "" }]
    );
  }

  function save(submit: boolean) {
    setError(null);
    if (!storeId) return setError(t("errorStore"));
    if (!prodId) return setError(t("errorProduction"));
    if (!neededBy) return setError(t("errorDate"));
    const parsed = lines.map((l) => ({ itemId: l.itemId, requestedQty: parseFloat(l.qty) }));
    if (parsed.length === 0) return setError(t("errorNoLines"));
    if (parsed.some((l) => !(l.requestedQty > 0))) return setError(t("errorQty"));

    const done = {
      onSuccess: onClose,
      onError: (e: unknown) => setError(errMessage(e, t("errorSave"))),
    };
    if (draft) {
      // Edit replaces the lines; submitting a draft is a separate action in the detail dialog.
      update.mutate({ neededBy, notes: notes.trim() || undefined, lines: parsed }, done);
    } else {
      create.mutate(
        {
          storeLocationId: storeId,
          productionLocationId: prodId,
          neededBy,
          notes: notes.trim() || undefined,
          lines: parsed,
          submit,
        },
        done
      );
    }
  }

  const pending = create.isPending || update.isPending;

  return (
    <Modal title={draft ? t("editTitle") : t("title")} onClose={onClose} width={640}>
      <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
          <div>
            <label style={reqLabelStyle}>{t("store")}</label>
            <select
              value={storeId}
              disabled={!!draft}
              onChange={(e) => setStoreId(e.target.value)}
              style={reqInputStyle}
            >
              <option value="">{t("select")}</option>
              {stores.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          </div>
          <div>
            <label style={reqLabelStyle}>{t("productionLocation")}</label>
            <select
              value={prodId}
              disabled={!!draft}
              onChange={(e) => setProdId(e.target.value)}
              style={reqInputStyle}
            >
              <option value="">{t("select")}</option>
              {prods.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
          </div>
        </div>

        <div>
          <label style={reqLabelStyle}>{t("neededBy")}</label>
          <input
            type="date"
            value={neededBy}
            onChange={(e) => setNeededBy(e.target.value)}
            style={reqInputStyle}
          />
        </div>

        <div>
          <label style={reqLabelStyle}>{t("addItem")}</label>
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t("searchPlaceholder")}
            style={reqInputStyle}
          />
          {debounced.trim() && found.length > 0 && search === debounced && (
            <div style={{ border: "1px solid #1F2937", borderRadius: 8, marginTop: 4, maxHeight: 180, overflowY: "auto" }}>
              {found.map((p) => (
                <button
                  key={p.id}
                  type="button"
                  onClick={() => addItem(p)}
                  style={{
                    display: "block",
                    width: "100%",
                    textAlign: "left",
                    padding: "7px 12px",
                    background: "transparent",
                    border: "none",
                    color: "#E8EDF5",
                    fontSize: 13,
                    cursor: "pointer",
                  }}
                >
                  {p.name} <span style={{ color: "#6B7280" }}>· {p.unit}</span>
                </button>
              ))}
            </div>
          )}
        </div>

        {lines.length === 0 ? (
          <div style={{ color: "#4B5563", fontSize: 12 }}>{t("noLines")}</div>
        ) : (
          <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
            {lines.map((l, i) => (
              <div key={l.itemId} style={{ display: "flex", gap: 8, alignItems: "center" }}>
                <div style={{ flex: 1 }}>
                  <span style={{ color: "#E8EDF5", fontSize: 13 }}>{l.itemName}</span>
                  <StockHint storeId={storeId} itemId={l.itemId} unit={l.unit} />
                </div>
                <input
                  type="number"
                  min={0}
                  step="any"
                  value={l.qty}
                  onChange={(e) =>
                    setLines((prev) => prev.map((x, j) => (j === i ? { ...x, qty: e.target.value } : x)))
                  }
                  placeholder={t("qty")}
                  style={{ ...reqInputStyle, width: 110 }}
                />
                <span style={{ color: "#6B7280", fontSize: 12, width: 36 }}>{l.unit}</span>
                <button
                  type="button"
                  onClick={() => setLines((prev) => prev.filter((_, j) => j !== i))}
                  aria-label={t("remove")}
                  style={{ background: "transparent", border: "none", color: "#F87171", cursor: "pointer" }}
                >
                  <Trash2 size={15} />
                </button>
              </div>
            ))}
          </div>
        )}

        <div>
          <label style={reqLabelStyle}>{t("notes")}</label>
          <textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={2}
            style={{ ...reqInputStyle, resize: "vertical" }}
          />
        </div>

        {error && <div style={reqErrorStyle}>{error}</div>}

        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10 }}>
          <Btn variant="ghost" onClick={onClose} disabled={pending}>{t("cancel")}</Btn>
          <Btn variant="primary" onClick={() => save(false)} disabled={pending}>
            {t("saveDraft")}
          </Btn>
          {!draft && (
            <Btn variant="success" onClick={() => save(true)} disabled={pending}>
              {t("submit")}
            </Btn>
          )}
        </div>
      </div>
    </Modal>
  );
}
