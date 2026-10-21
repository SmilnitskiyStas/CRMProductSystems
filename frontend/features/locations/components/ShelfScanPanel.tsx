"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Camera } from "lucide-react";
import { Modal } from "@/components/ui/Modal";
import { Btn } from "@/components/ui/Btn";
import { API_BASE, ApiError } from "@/lib/api";
import { useCreateShelfScan } from "../hooks/useShelves";
import type { ShelfRecommendationDto, ShelfScanDto, ShelfScanItemDto, ShelfScanItemStatus } from "../types";

const MAX_SIDE = 1600;
const MAX_BYTES = 5 * 1024 * 1024;

export const SCAN_STATUS_COLOR: Record<ShelfScanItemStatus, { fg: string; bg: string; border: string }> = {
  present: { fg: "#4ADE80", bg: "#0F2D1A", border: "#166534" },
  low: { fg: "#FCD34D", bg: "#2A2000", border: "#854D0E" },
  missing: { fg: "#F87171", bg: "#2D0F0F", border: "#991B1B" },
};

export function resolveScanImageUrl(value: string): string {
  if (/^https?:\/\//i.test(value)) return value;
  return `${API_BASE.replace(/\/$/, "")}/${value.replace(/^\//, "")}`;
}

/** Downscales the photo to ~1600px JPEG so phone-camera originals stay under the 5 MB limit. */
async function downscaleImage(file: File): Promise<Blob> {
  try {
    const bitmap = await createImageBitmap(file);
    const ratio = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
    const canvas = document.createElement("canvas");
    canvas.width = Math.round(bitmap.width * ratio);
    canvas.height = Math.round(bitmap.height * ratio);
    const ctx = canvas.getContext("2d");
    if (!ctx) return file;
    ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    bitmap.close();
    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", 0.85));
    return blob ?? file;
  } catch {
    return file;
  }
}

interface Props {
  onClose: () => void;
  locationId: string;
  zoneId: string;
  sections: { shelfId: string; label: string }[];
  scans: ShelfScanDto[];
  activeScan: ShelfScanDto | null;
  onSelectScan: (scanId: string) => void;
}

export function ShelfScanPanel({ onClose, locationId, zoneId, sections, scans, activeScan, onSelectScan }: Props) {
  const t = useTranslations("Dashboard.locations.shelvesPage");
  const create = useCreateShelfScan(locationId, zoneId);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const [shelfId, setShelfId] = useState<string>("");

  function errorMessage(err: unknown): string {
    if (err instanceof ApiError) {
      switch (err.status) {
        case 400: return t("scanErr400");
        case 413: return t("scanErr400");
        case 422: return t("scanErr422");
        case 429: return t("scanErr429");
        case 502: return t("scanErr502");
        case 503: return t("scanErr503");
      }
    }
    return err instanceof Error ? err.message : String(err);
  }

  async function handleFile(file: File | undefined) {
    if (!file) return;
    const blob = await downscaleImage(file);
    if (blob.size > MAX_BYTES) {
      toast.error(t("scanErr400"));
      return;
    }
    create.mutate(
      { file: blob, shelfId: shelfId || null },
      {
        onSuccess: (scan) => {
          onSelectScan(scan.id);
          toast.success(t("scanDone"));
        },
        onError: (err) => toast.error(errorMessage(err)),
      },
    );
    if (inputRef.current) inputRef.current.value = "";
  }

  return (
    <Modal title={t("scanTitle")} onClose={onClose} width={720}>
      <div style={{ display: "flex", flexDirection: "column", gap: 16 }}>
        <div style={{ display: "flex", gap: 10, alignItems: "center", flexWrap: "wrap" }}>
          <select
            value={shelfId}
            onChange={(e) => setShelfId(e.target.value)}
            disabled={create.isPending}
            style={{
              background: "#161B26",
              border: "1px solid #1F2937",
              borderRadius: 8,
              color: "#E8EDF5",
              padding: "7px 10px",
              fontSize: 13,
            }}
          >
            <option value="">{t("scanWholeZone")}</option>
            {sections.map((s) => (
              <option key={s.shelfId} value={s.shelfId}>{s.label}</option>
            ))}
          </select>
          <input
            ref={inputRef}
            type="file"
            accept="image/jpeg,image/png,image/webp,image/*"
            capture="environment"
            hidden
            onChange={(e) => handleFile(e.target.files?.[0])}
          />
          <Btn onClick={() => inputRef.current?.click()} disabled={create.isPending} icon={<Camera size={14} />}>
            {create.isPending ? t("scanAnalyzing") : t("scanChoosePhoto")}
          </Btn>
        </div>
        <p style={{ color: "#6B7280", fontSize: 12, margin: 0 }}>{t("scanHint")}</p>

        {activeScan && <ScanResult scan={activeScan} />}

        <div>
          <div style={{ color: "#4B5563", fontSize: 11, fontWeight: 600, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 6 }}>
            {t("scanHistory")}
          </div>
          {scans.length === 0 ? (
            <p style={{ color: "#4B5563", fontSize: 12, margin: 0 }}>{t("scanHistoryEmpty")}</p>
          ) : (
            <div style={{ display: "flex", flexDirection: "column", gap: 6, maxHeight: 220, overflowY: "auto" }}>
              {scans.map((s) => (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => onSelectScan(s.id)}
                  style={{
                    display: "flex",
                    alignItems: "center",
                    gap: 10,
                    textAlign: "left",
                    background: activeScan?.id === s.id ? "#0d1f3a" : "#0B0E14",
                    border: `1px solid ${activeScan?.id === s.id ? "#1D4ED8" : "#1F2937"}`,
                    borderRadius: 8,
                    padding: 6,
                    cursor: "pointer",
                  }}
                >
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    src={resolveScanImageUrl(s.imageUrl)}
                    alt=""
                    style={{ width: 48, height: 48, objectFit: "cover", borderRadius: 6, background: "#161B26" }}
                  />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ color: "#E8EDF5", fontSize: 12 }}>
                      {new Date(s.createdAt).toLocaleString("uk-UA")}
                    </div>
                    <div style={{ fontSize: 11, color: "#9CA3AF" }}>
                      {s.status === "failed"
                        ? t("scanFailed")
                        : t("scanCounts", { missing: s.missingCount, low: s.lowCount })}
                    </div>
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
}

function ScanResult({ scan }: { scan: ShelfScanDto }) {
  const t = useTranslations("Dashboard.locations.shelvesPage");
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8, background: "#161B26", border: "1px solid #1F2937", borderRadius: 10, padding: 12 }}>
      {scan.status !== "failed" && scan.summary && (
        <div style={{ color: "#E8EDF5", fontSize: 13, background: "#0d1f3a", border: "1px solid #1D4ED8", borderRadius: 8, padding: "8px 10px" }}>
          <div style={{ color: "#60A5FA", fontSize: 10, fontWeight: 700, textTransform: "uppercase", letterSpacing: "0.05em", marginBottom: 2 }}>
            {t("scanSummaryTitle")}
          </div>
          {scan.summary}
        </div>
      )}
      <div style={{ display: "flex", gap: 12 }}>
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={resolveScanImageUrl(scan.imageUrl)}
          alt=""
          style={{ width: 120, height: 120, objectFit: "cover", borderRadius: 8, background: "#0B0E14" }}
        />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
            {t("scanResultTitle")}
          </div>
          {scan.status === "failed" ? (
            <p style={{ color: "#F87171", fontSize: 12, margin: "4px 0 0" }}>
              {scan.errorMessage ?? t("scanFailed")}
            </p>
          ) : (
            <>
              <p style={{ color: "#9CA3AF", fontSize: 12, margin: "4px 0 0" }}>
                {t("scanCounts", { missing: scan.missingCount, low: scan.lowCount })}
              </p>
            </>
          )}
          <p style={{ color: "#FCD34D", fontSize: 11, margin: "6px 0 0" }}>{t("scanIndicative")}</p>
        </div>
      </div>
      {scan.items.length > 0 && (
        <div style={{ display: "flex", flexDirection: "column", gap: 4, maxHeight: 360, overflowY: "auto" }}>
          {scan.items.map((it) => {
            const c = SCAN_STATUS_COLOR[it.status];
            return (
              <div
                key={it.itemId}
                style={{ padding: "5px 8px", background: "#0B0E14", border: "1px solid #1F2937", borderRadius: 6, fontSize: 12 }}
              >
                <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <span
                  style={{ background: c.bg, border: `1px solid ${c.border}`, color: c.fg, borderRadius: 20, padding: "1px 8px", fontSize: 10, fontWeight: 700, whiteSpace: "nowrap" }}
                >
                  {t(`scanStatus_${it.status}`)}
                </span>
                <span style={{ flex: 1, minWidth: 0, color: "#E8EDF5", overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                  {it.name}
                  {it.note && <span style={{ color: "#6B7280" }}> — {it.note}</span>}
                </span>
                <span style={{ color: "#9CA3AF", fontFamily: "monospace", whiteSpace: "nowrap" }}>
                  ~{it.estimatedCount}
                </span>
                <span style={{ color: "#6B7280", fontFamily: "monospace", whiteSpace: "nowrap" }} title={t("scanIndicative")}>
                  {Math.round(it.confidence * 100)}%
                </span>
              </div>
                {it.status !== "present" && <ScanItemDetails it={it} />}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

const PRIORITY_COLOR: Record<ShelfRecommendationDto["priority"], string> = {
  high: "#F87171",
  medium: "#FCD34D",
  low: "#9CA3AF",
};

function qty(v: number | null | undefined): string {
  return v === null || v === undefined ? "—" : String(Math.round(v * 10) / 10);
}

function ScanItemDetails({ it }: { it: ShelfScanItemDto }) {
  const t = useTranslations("Dashboard.locations.shelvesPage");
  const hasData = [it.onShelfQty, it.storeQty, it.warehouseQty, it.sold7d, it.sold30d, it.daysOfCover].some(
    (v) => v !== null && v !== undefined,
  );
  const rec = it.recommendation;
  if (!hasData && (!rec || rec.action === "none")) return null;
  const inStockElsewhere = (it.warehouseQty ?? 0) > 0 && (it.onShelfQty ?? 0) <= 0;
  return (
    <div style={{ marginTop: 4, paddingTop: 4, borderTop: "1px dashed #1F2937", display: "flex", flexDirection: "column", gap: 3 }}>
      {hasData && (
        <div style={{ display: "flex", flexWrap: "wrap", gap: "2px 12px", color: "#9CA3AF", fontSize: 11 }}>
          <span>{t("scanOnShelf")}: <b style={{ color: "#E8EDF5" }}>{qty(it.onShelfQty)}</b></span>
          <span>{t("scanInStore")}: <b style={{ color: "#E8EDF5" }}>{qty(it.storeQty)}</b></span>
          <span>
            {t("scanInWarehouse")}: <b style={{ color: inStockElsewhere ? "#FCD34D" : "#E8EDF5" }}>{qty(it.warehouseQty)}</b>
          </span>
          <span>{t("scanSold")}: <b style={{ color: "#E8EDF5" }}>{qty(it.sold7d)} / {qty(it.sold30d)}</b></span>
          <span>{t("scanDaysOfCover")}: <b style={{ color: "#E8EDF5" }}>{qty(it.daysOfCover)}</b></span>
        </div>
      )}
      {rec && rec.action !== "none" && (
        <div style={{ fontSize: 11, color: PRIORITY_COLOR[rec.priority], fontWeight: 600 }}>
          {t(`scanRec_${rec.action}`, { qty: rec.suggestedQty })} · {t(`scanPriority_${rec.priority}`)}
        </div>
      )}
    </div>
  );
}
