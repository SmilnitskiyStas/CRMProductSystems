"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useCreateSupplier } from "../hooks/useMarketplace";
import type { CreateSupplierRequest, SupplierPlan } from "../types";
import { Btn } from "@/components/ui/Btn";

interface Props {
  onClose: () => void;
}

export function CreateSupplierModal({ onClose }: Props) {
  const createSupplier = useCreateSupplier();

  const [companyName, setCompanyName] = useState("");
  const [region, setRegion] = useState("");
  const [categoriesRaw, setCategoriesRaw] = useState("");
  const [isPublic, setIsPublic] = useState(true);
  const [plan, setPlan] = useState<SupplierPlan>("free");
  const [error, setError] = useState<string | null>(null);

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);

    if (!companyName.trim()) {
      setError("Назва компанії є обов’язковою.");
      return;
    }

    const categories = categoriesRaw
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);

    const body: CreateSupplierRequest = {
      companyName: companyName.trim(),
      region: region.trim() || undefined,
      categories: categories.length > 0 ? categories : undefined,
      isPublic,
      plan,
    };

    createSupplier.mutate(body, {
      onSuccess: () => onClose(),
      onError: (err: any) => {
        setError(err?.message ?? "Помилка при створенні постачальника.");
      },
    });
  }

  const inputStyle: React.CSSProperties = {
    width: "100%",
    padding: "9px 12px",
    background: "#0D1117",
    border: "1px solid #1F2937",
    borderRadius: 8,
    color: "#E8EDF5",
    fontSize: 13,
    outline: "none",
    boxSizing: "border-box",
  };

  const labelStyle: React.CSSProperties = {
    color: "#9CA3AF",
    fontSize: 12,
    fontWeight: 500,
    marginBottom: 6,
    display: "block",
  };

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.6)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: 28,
          width: "100%",
          maxWidth: 480,
          display: "flex",
          flexDirection: "column",
          gap: 20,
        }}
      >
        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 17, fontWeight: 700, margin: 0 }}>
            Створити постачальника
          </h2>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "none",
              color: "#6B7280",
              cursor: "pointer",
              padding: 4,
            }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 16 }}>
          <div>
            <label style={labelStyle}>
              Назва компанії <span style={{ color: "#F87171" }}>*</span>
            </label>
            <input
              type="text"
              value={companyName}
              onChange={(e) => setCompanyName(e.target.value)}
              placeholder="ТОВ «Молокозавод Плюс»"
              style={inputStyle}
              required
            />
          </div>

          <div>
            <label style={labelStyle}>Регіон</label>
            <input
              type="text"
              value={region}
              onChange={(e) => setRegion(e.target.value)}
              placeholder="Київська область"
              style={inputStyle}
            />
          </div>

          <div>
            <label style={labelStyle}>Категорії (через кому)</label>
            <input
              type="text"
              value={categoriesRaw}
              onChange={(e) => setCategoriesRaw(e.target.value)}
              placeholder="Молочні продукти, Хліб, Заморожені товари"
              style={inputStyle}
            />
          </div>

          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <input
              type="checkbox"
              id="isPublic"
              checked={isPublic}
              onChange={(e) => setIsPublic(e.target.checked)}
              style={{ width: 16, height: 16, cursor: "pointer" }}
            />
            <label
              htmlFor="isPublic"
              style={{ color: "#E8EDF5", fontSize: 13, cursor: "pointer" }}
            >
              Показувати в маркетплейсі
            </label>
          </div>

          <div>
            <label style={labelStyle}>Тарифний план</label>
            <select
              value={plan}
              onChange={(e) => setPlan(e.target.value as SupplierPlan)}
              style={{ ...inputStyle, cursor: "pointer" }}
            >
              <option value="free">Free</option>
              <option value="premium">Premium</option>
            </select>
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 13 }}>{error}</div>
          )}

          <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 4 }}>
            <Btn type="button" variant="ghost" onClick={onClose}>
              Скасувати
            </Btn>
            <Btn type="submit" disabled={createSupplier.isPending}>
              {createSupplier.isPending ? "Збереження…" : "Створити"}
            </Btn>
          </div>
        </form>
      </div>
    </div>
  );
}
