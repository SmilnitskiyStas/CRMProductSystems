"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ChevronDown, Store } from "lucide-react";
import { useStores } from "@/features/stores/hooks/useStores";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useStoreContext } from "@/lib/useStoreContext";

export function StoreSelector() {
  const t = useTranslations("Dashboard.storeSelector");
  const { data: stores = [] } = useStores();
  const { data: user } = useMe();
  const { selectedStoreIds, setSelectedStoreIds, initialized, setInitialized, hasHydrated } =
    useStoreContext();
  const [open, setOpen] = useState(false);
  const [draftStoreIds, setDraftStoreIds] = useState<string[]>(selectedStoreIds);
  const ref = useRef<HTMLDivElement>(null);

  // Resolve the one-time default (prefer user.storeId, else first store) exactly like before —
  // but only once, ever. After that, an explicit "all stores" (empty array) selection is left
  // alone instead of being clobbered back to a single store on every stores refetch.
  useEffect(() => {
    // Wait for persist to rehydrate from localStorage first — otherwise `initialized` can
    // still read its in-memory default (false) before the real persisted value lands, and
    // this would clobber an explicit "all stores" choice back to a single store.
    if (!hasHydrated) return;
    if (stores.length === 0) return;
    if (!initialized) {
      const preferred = user?.storeId;
      const valid = stores.some((s) => s.id === preferred) ? preferred! : stores[0].id;
      setSelectedStoreIds([valid]);
      setInitialized(true);
      return;
    }
    if (selectedStoreIds.length === 0) return; // explicit "all stores" — leave alone
    const pruned = selectedStoreIds.filter((id) => stores.some((s) => s.id === id));
    if (pruned.length !== selectedStoreIds.length) setSelectedStoreIds(pruned);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stores, user?.storeId, initialized, hasHydrated]);

  // Sync the draft selection whenever the popover opens.
  useEffect(() => {
    if (open) setDraftStoreIds(selectedStoreIds);
  }, [open, selectedStoreIds]);

  // Close on outside click
  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, []);

  function toggleDraftStore(id: string) {
    setDraftStoreIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  function selectAllStores() {
    setDraftStoreIds([]);
  }

  function applyStores() {
    setSelectedStoreIds([...draftStoreIds].sort());
    setOpen(false);
  }

  const label =
    selectedStoreIds.length === 0
      ? t("allStores")
      : selectedStoreIds.length === 1
      ? stores.find((s) => s.id === selectedStoreIds[0])?.name ?? t("storesCount", { count: 1 })
      : t("storesCount", { count: selectedStoreIds.length });

  if (stores.length === 0) return null;

  return (
    <div ref={ref} style={{ position: "relative" }}>
      <button
        onClick={() => setOpen((v) => !v)}
        style={{
          display: "flex",
          alignItems: "center",
          gap: 8,
          background: open ? "#161B26" : "transparent",
          border: `1px solid ${open ? "#374151" : "#1F2937"}`,
          borderRadius: 8,
          padding: "5px 10px 5px 8px",
          cursor: "pointer",
          color: "#E8EDF5",
          transition: "border-color 0.15s, background 0.15s",
        }}
        onMouseEnter={(e) => {
          if (!open) (e.currentTarget as HTMLElement).style.borderColor = "#374151";
        }}
        onMouseLeave={(e) => {
          if (!open) (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
        }}
      >
        <Store size={14} color="#6B7280" />
        <span style={{ fontSize: 13, fontWeight: 600, maxWidth: 160, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
          {label}
        </span>
        <ChevronDown
          size={13}
          color="#6B7280"
          style={{ transition: "transform 0.15s", transform: open ? "rotate(180deg)" : "none", flexShrink: 0 }}
        />
      </button>

      {open && (
        <div
          style={{
            position: "absolute",
            top: "calc(100% + 6px)",
            left: 0,
            minWidth: 240,
            background: "#0D1117",
            border: "1px solid #1F2937",
            borderRadius: 10,
            boxShadow: "0 8px 24px rgba(0,0,0,0.5)",
            zIndex: 200,
            overflow: "hidden",
          }}
        >
          <div style={{ padding: "6px 12px 4px", borderBottom: "1px solid #1F2937" }}>
            <span style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.06em" }}>
              {t("choose")}
            </span>
          </div>
          <div style={{ maxHeight: 280, overflowY: "auto" }}>
            {stores.length > 1 && (
              <label
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 10,
                  padding: "9px 12px",
                  borderBottom: "1px solid #111827",
                  cursor: "pointer",
                }}
              >
                <input
                  type="checkbox"
                  checked={draftStoreIds.length === 0}
                  onChange={selectAllStores}
                  style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer" }}
                />
                <span
                  style={{
                    color: draftStoreIds.length === 0 ? "#E8EDF5" : "#9CA3AF",
                    fontSize: 13,
                    fontWeight: draftStoreIds.length === 0 ? 600 : 400,
                  }}
                >
                  {t("selectAllStores")}
                </span>
              </label>
            )}
            {stores.map((store) => {
              const checked = draftStoreIds.includes(store.id);
              return (
                <label
                  key={store.id}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 10,
                    padding: "9px 12px",
                    borderBottom: "1px solid #111827",
                    cursor: "pointer",
                    transition: "background 0.1s",
                  }}
                >
                  <input
                    type="checkbox"
                    checked={checked}
                    onChange={() => toggleDraftStore(store.id)}
                    style={{ accentColor: "#3B82F6", width: 14, height: 14, cursor: "pointer", flexShrink: 0 }}
                  />
                  <div style={{
                    width: 28, height: 28, borderRadius: 7, flexShrink: 0,
                    background: checked ? "#1D3461" : "#161B26",
                    border: `1px solid ${checked ? "#3B82F640" : "#1F2937"}`,
                    display: "flex", alignItems: "center", justifyContent: "center",
                  }}>
                    <Store size={13} color={checked ? "#60A5FA" : "#4B5563"} />
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{
                      color: checked ? "#E8EDF5" : "#9CA3AF",
                      fontSize: 13, fontWeight: checked ? 600 : 400,
                      overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap",
                    }}>
                      {store.name}
                    </div>
                    {store.address && (
                      <div style={{ color: "#4B5563", fontSize: 11, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                        {store.address}
                      </div>
                    )}
                  </div>
                </label>
              );
            })}
          </div>
          <div style={{ padding: 10, borderTop: "1px solid #1F2937" }}>
            <button
              onClick={applyStores}
              style={{
                width: "100%",
                background: "#1D3461",
                border: "1px solid #3B82F6",
                borderRadius: 7,
                color: "#93C5FD",
                fontSize: 12,
                fontWeight: 600,
                padding: "8px 0",
                cursor: "pointer",
              }}
            >
              {t("doneButton")}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
