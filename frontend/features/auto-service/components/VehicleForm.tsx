"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { useCreateVehicle, useUpdateVehicle } from "../hooks/useAutoService";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { inputStyle, labelStyle } from "@/components/ui/fieldStyles";
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
    <Modal title={isEdit ? t("titleEdit") : t("titleNew")} onClose={onClose} width={480}>
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
            <Btn variant="ghost" onClick={onClose}>{t("cancel")}</Btn>
            <Btn type="submit" disabled={isPending}>
              {isPending ? t("saving") : isEdit ? t("save") : t("add")}
            </Btn>
          </div>
        </form>
    </Modal>
  );
}
