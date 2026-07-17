"use client";

import { useState } from "react";
import { X } from "lucide-react";
import { useTranslations } from "next-intl";
import { useCreateVehicle, useUpdateVehicle } from "../hooks/useAutoService";
import type { VehicleDto, CreateVehicleRequest, UpdateVehicleRequest } from "../types";

interface Props {
  customerId: string;
  vehicle?: VehicleDto;
  onClose: () => void;
}

export function VehicleForm({ customerId, vehicle, onClose }: Props) {
  const t = useTranslations("Dashboard.autoService.vehicleForm");
  const createVehicle = useCreateVehicle();
  const updateVehicle = useUpdateVehicle(vehicle?.id ?? "");

  const [brand, setBrand] = useState(vehicle?.brand ?? "");
  const [model, setModel] = useState(vehicle?.model ?? "");
  const [year, setYear] = useState<number | "">(vehicle?.year ?? "");
  const [vin, setVin] = useState(vehicle?.vin ?? "");
  const [licensePlate, setLicensePlate] = useState(vehicle?.licensePlate ?? "");
  const [mileage, setMileage] = useState<number | "">(vehicle?.mileage ?? "");
  const [error, setError] = useState<string | null>(null);

  const isEdit = !!vehicle;
  const isPending = createVehicle.isPending || updateVehicle.isPending;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    if (!brand.trim()) { setError(t("errorBrand")); return; }
    if (!model.trim()) { setError(t("errorModel")); return; }
    if (!licensePlate.trim()) { setError(t("errorPlate")); return; }

    if (isEdit) {
      const body: UpdateVehicleRequest = {
        brand: brand.trim(),
        model: model.trim(),
        year: year !== "" ? Number(year) : undefined,
        vin: vin.trim() || undefined,
        licensePlate: licensePlate.trim(),
        mileage: mileage !== "" ? Number(mileage) : undefined,
      };
      updateVehicle.mutate(body, {
        onSuccess: () => onClose(),
        onError: (err) => setError(err instanceof Error ? err.message : t("errorGeneric")),
      });
    } else {
      const body: CreateVehicleRequest = {
        customerId,
        brand: brand.trim(),
        model: model.trim(),
        year: year !== "" ? Number(year) : undefined,
        vin: vin.trim() || undefined,
        licensePlate: licensePlate.trim(),
        mileage: mileage !== "" ? Number(mileage) : undefined,
      };
      createVehicle.mutate(body, {
        onSuccess: () => onClose(),
        onError: (err) => setError(err instanceof Error ? err.message : t("errorGeneric")),
      });
    }
  }

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.7)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 1000,
      }}
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        style={{
          background: "#111827",
          border: "1px solid #1F2937",
          borderRadius: 12,
          padding: "24px",
          width: "100%",
          maxWidth: 480,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", marginBottom: 20 }}>
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 700, margin: 0 }}>
            {isEdit ? t("titleEdit") : t("titleNew")}
          </h2>
          <button onClick={onClose} style={{ background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}>
            <X size={18} />
          </button>
        </div>

        <form onSubmit={handleSubmit} style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <div>
              <label style={labelStyle}>{t("brandLabel")}</label>
              <input type="text" value={brand} onChange={(e) => setBrand(e.target.value)} placeholder="Toyota" style={inputStyle} />
            </div>
            <div>
              <label style={labelStyle}>{t("modelLabel")}</label>
              <input type="text" value={model} onChange={(e) => setModel(e.target.value)} placeholder="Camry" style={inputStyle} />
            </div>
          </div>

          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
            <div>
              <label style={labelStyle}>{t("yearLabel")}</label>
              <input
                type="number"
                min={1900}
                max={new Date().getFullYear() + 1}
                value={year}
                onChange={(e) => setYear(e.target.value ? Number(e.target.value) : "")}
                placeholder="2020"
                style={inputStyle}
              />
            </div>
            <div>
              <label style={labelStyle}>{t("plateLabel")}</label>
              <input type="text" value={licensePlate} onChange={(e) => setLicensePlate(e.target.value)} placeholder="AA 1234 BB" style={inputStyle} />
            </div>
          </div>

          <div>
            <label style={labelStyle}>{t("vinLabel")}</label>
            <input type="text" value={vin} onChange={(e) => setVin(e.target.value)} placeholder="1HGBH41JXMN109186" maxLength={17} style={inputStyle} />
          </div>

          <div>
            <label style={labelStyle}>{t("mileageLabel")}</label>
            <input
              type="number"
              min={0}
              value={mileage}
              onChange={(e) => setMileage(e.target.value ? Number(e.target.value) : "")}
              placeholder="85000"
              style={inputStyle}
            />
          </div>

          {error && (
            <div style={{ color: "#F87171", fontSize: 12, padding: "8px 12px", background: "#1F1010", borderRadius: 6 }}>
              {error}
            </div>
          )}

          <div style={{ display: "flex", gap: 8, justifyContent: "flex-end", marginTop: 4 }}>
            <button type="button" onClick={onClose} style={btnSecondaryStyle}>{t("cancel")}</button>
            <button type="submit" disabled={isPending} style={btnPrimaryStyle}>
              {isPending ? t("saving") : isEdit ? t("save") : t("add")}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

const labelStyle: React.CSSProperties = { color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" };
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
const btnPrimaryStyle: React.CSSProperties = { padding: "9px 20px", borderRadius: 8, border: "none", background: "#1D4ED8", color: "#E8EDF5", fontSize: 13, fontWeight: 600, cursor: "pointer" };
const btnSecondaryStyle: React.CSSProperties = { padding: "9px 16px", borderRadius: 8, border: "1px solid #1F2937", background: "transparent", color: "#6B7280", fontSize: 13, cursor: "pointer" };
