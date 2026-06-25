"use client";

import { useEffect, useRef, useState } from "react";
import { ChevronDown, Store, Check } from "lucide-react";
import { useStores } from "@/features/stores/hooks/useStores";
import { useMe } from "@/features/auth/hooks/useAuth";
import { useStoreContext } from "@/lib/useStoreContext";

export function StoreSelector() {
  const { data: stores = [] } = useStores();
  const { data: user } = useMe();
  const { selectedStoreId, setSelectedStoreId } = useStoreContext();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  // Initialize: prefer user.storeId, then persisted selection, then first store
  useEffect(() => {
    if (stores.length === 0) return;
    const preferred = user?.storeId ?? selectedStoreId;
    const valid = stores.find((s) => s.id === preferred);
    setSelectedStoreId(valid?.id ?? stores[0].id);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stores, user?.storeId]);

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

  const selectedStore = stores.find((s) => s.id === selectedStoreId) ?? stores[0];

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
          {selectedStore?.name ?? "Магазин"}
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
            minWidth: 220,
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
              Оберіть магазин
            </span>
          </div>
          <div style={{ maxHeight: 280, overflowY: "auto" }}>
            {stores.map((store) => {
              const active = store.id === selectedStoreId;
              return (
                <button
                  key={store.id}
                  onClick={() => { setSelectedStoreId(store.id); setOpen(false); }}
                  style={{
                    width: "100%",
                    display: "flex",
                    alignItems: "center",
                    gap: 10,
                    padding: "9px 12px",
                    background: active ? "#161B26" : "transparent",
                    border: "none",
                    borderBottom: "1px solid #111827",
                    cursor: "pointer",
                    textAlign: "left",
                    transition: "background 0.1s",
                  }}
                  onMouseEnter={(e) => {
                    if (!active) (e.currentTarget as HTMLElement).style.background = "#0F1623";
                  }}
                  onMouseLeave={(e) => {
                    if (!active) (e.currentTarget as HTMLElement).style.background = "transparent";
                  }}
                >
                  <div style={{
                    width: 28, height: 28, borderRadius: 7, flexShrink: 0,
                    background: active ? "#1D3461" : "#161B26",
                    border: `1px solid ${active ? "#3B82F640" : "#1F2937"}`,
                    display: "flex", alignItems: "center", justifyContent: "center",
                  }}>
                    <Store size={13} color={active ? "#60A5FA" : "#4B5563"} />
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{
                      color: active ? "#E8EDF5" : "#9CA3AF",
                      fontSize: 13, fontWeight: active ? 600 : 400,
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
                  {active && <Check size={14} color="#3B82F6" style={{ flexShrink: 0 }} />}
                </button>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
