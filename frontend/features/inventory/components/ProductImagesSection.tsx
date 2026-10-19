"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { API_BASE } from "@/lib/api";
import {
  useAddItemImage,
  useDeleteItemImage,
  useItemImages,
  useUpdateItemImage,
} from "../hooks/useProducts";
import type { ItemImage } from "../types";

const MAX_IMAGES = 8;
const MAX_SIDE = 1600;
const MAX_LABEL = 50;

interface Props {
  productId: string;
  /** Editing controls are hidden when false (read-only roles). */
  canEdit?: boolean;
}

const assetUrl = (value: string) =>
  /^https?:\/\//i.test(value) ? value : `${API_BASE.replace(/\/$/, "")}/${value.replace(/^\//, "")}`;

// Downscale to max 1600px on the longest side and re-encode as JPEG (backend limit is 5 MB).
async function downscale(file: File): Promise<File> {
  const bitmap = await createImageBitmap(file);
  const ratio = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(bitmap.width * ratio);
  canvas.height = Math.round(bitmap.height * ratio);
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("canvas");
  ctx.fillStyle = "#fff"; // JPEG has no alpha
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  bitmap.close();
  const blob = await new Promise<Blob | null>((res) => canvas.toBlob(res, "image/jpeg", 0.85));
  if (!blob) throw new Error("toBlob");
  return new File([blob], file.name.replace(/\.[^.]+$/, "") + ".jpg", { type: "image/jpeg" });
}

const inputStyle: React.CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 12,
  padding: "5px 8px",
  outline: "none",
  boxSizing: "border-box",
};

const chipStyle: React.CSSProperties = {
  padding: "3px 8px",
  borderRadius: 6,
  cursor: "pointer",
  background: "#0F1F3D",
  border: "1px solid #1E3A5F",
  color: "#93C5FD",
  fontSize: 11,
};

const smallBtn: React.CSSProperties = {
  background: "none",
  border: "none",
  cursor: "pointer",
  padding: 0,
  fontSize: 11,
};

function ImageCard({
  image, canEdit, busy, onMakePrimary, onDelete, onSaveLabel,
}: {
  image: ItemImage;
  canEdit: boolean;
  busy: boolean;
  onMakePrimary: () => void;
  onDelete: () => void;
  onSaveLabel: (label: string) => void;
}) {
  const t = useTranslations("Dashboard.inventory.itemImages");
  const [label, setLabel] = useState(image.label ?? "");
  const dirty = label.trim() !== (image.label ?? "");

  return (
    <div
      style={{
        border: `1px solid ${image.isPrimary ? "#166534" : "#1F2937"}`,
        borderRadius: 8,
        padding: 6,
        display: "flex",
        flexDirection: "column",
        gap: 6,
        opacity: busy ? 0.6 : 1,
      }}
    >
      <div style={{ position: "relative" }}>
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={assetUrl(image.url)}
          alt={image.label ?? ""}
          style={{ width: "100%", aspectRatio: "1 / 1", objectFit: "cover", borderRadius: 6, display: "block" }}
        />
        {image.isPrimary && (
          <span
            style={{
              position: "absolute", top: 4, left: 4, background: "#052e16", color: "#4ADE80",
              border: "1px solid #166534", borderRadius: 4, fontSize: 10, padding: "1px 6px",
            }}
          >
            ★ {t("primary")}
          </span>
        )}
      </div>

      {canEdit ? (
        <>
          <div style={{ display: "flex", gap: 4 }}>
            <input
              value={label}
              maxLength={MAX_LABEL}
              placeholder={t("labelPlaceholder")}
              onChange={(e) => setLabel(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault();
                  if (dirty) onSaveLabel(label.trim());
                }
              }}
              style={inputStyle}
            />
            {dirty && (
              <button
                type="button"
                title={t("labelSave")}
                onClick={() => onSaveLabel(label.trim())}
                style={{ ...smallBtn, color: "#4ADE80", fontSize: 14 }}
              >
                ✓
              </button>
            )}
          </div>
          <div style={{ display: "flex", justifyContent: "space-between", gap: 6 }}>
            {image.isPrimary ? (
              <span />
            ) : (
              <button type="button" onClick={onMakePrimary} disabled={busy} style={{ ...smallBtn, color: "#60A5FA" }}>
                ☆ {t("makePrimary")}
              </button>
            )}
            <button type="button" onClick={onDelete} disabled={busy} style={{ ...smallBtn, color: "#EF4444" }}>
              {t("delete")}
            </button>
          </div>
        </>
      ) : (
        image.label && <span style={{ color: "#9CA3AF", fontSize: 11 }}>{image.label}</span>
      )}
    </div>
  );
}

// Multi-photo gallery for a product (different sides). Own widget with independent mutations —
// not part of the product form's submit. Server enforces max 8 / 5 MB; we downscale client-side.
export function ProductImagesSection({ productId, canEdit = true }: Props) {
  const t = useTranslations("Dashboard.inventory.itemImages");
  const { data: images = [], isLoading } = useItemImages(productId);
  const addImage = useAddItemImage(productId);
  const updateImage = useUpdateItemImage(productId);
  const deleteImage = useDeleteItemImage(productId);

  const fileRef = useRef<HTMLInputElement>(null);
  const [pendingLabel, setPendingLabel] = useState("");
  const [uploading, setUploading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const full = images.length >= MAX_IMAGES;

  async function handleFile(file: File) {
    setError(null);
    setUploading(true);
    try {
      let prepared: File;
      try {
        prepared = await downscale(file);
      } catch {
        setError(t("readError"));
        return;
      }
      await addImage.mutateAsync({ file: prepared, label: pendingLabel.trim() || undefined });
      setPendingLabel("");
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t("error"));
    } finally {
      setUploading(false);
      if (fileRef.current) fileRef.current.value = "";
    }
  }

  async function run(imageId: string, fn: () => Promise<unknown>) {
    setError(null);
    setBusyId(imageId);
    try {
      await fn();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t("error"));
    } finally {
      setBusyId(null);
    }
  }

  if (isLoading) {
    return <p style={{ color: "#4B5563", fontSize: 12, margin: 0 }}>{t("loading")}</p>;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
      {canEdit && <p style={{ color: "#4B5563", fontSize: 11, margin: 0, lineHeight: 1.5 }}>{t("hint")}</p>}

      {images.length === 0 ? (
        <p style={{ color: "#4B5563", fontSize: 12, margin: 0 }}>{t("empty")}</p>
      ) : (
        <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(120px, 1fr))", gap: 8 }}>
          {images.map((img) => (
            <ImageCard
              key={`${img.id}:${img.label ?? ""}`}
              image={img}
              canEdit={canEdit}
              busy={busyId === img.id}
              onMakePrimary={() => run(img.id, () => updateImage.mutateAsync({ imageId: img.id, payload: { isPrimary: true } }))}
              onSaveLabel={(label) => run(img.id, () => updateImage.mutateAsync({ imageId: img.id, payload: { label } }))}
              onDelete={() => {
                if (window.confirm(t("deleteConfirm"))) run(img.id, () => deleteImage.mutateAsync(img.id));
              }}
            />
          ))}
        </div>
      )}

      {canEdit && (
        <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
          <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 6 }}>
            {(["chipFront", "chipSide", "chipBack"] as const).map((k) => (
              <button key={k} type="button" style={chipStyle} onClick={() => setPendingLabel(t(k))}>
                {t(k)}
              </button>
            ))}
            <input
              value={pendingLabel}
              maxLength={MAX_LABEL}
              placeholder={t("pendingLabel")}
              onChange={(e) => setPendingLabel(e.target.value)}
              style={{ ...inputStyle, flex: 1, minWidth: 140, padding: "6px 8px" }}
            />
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <label
              style={{
                padding: "8px 14px", borderRadius: 8, fontSize: 12,
                cursor: full || uploading ? "default" : "pointer",
                background: "#111827", border: "1px solid #374151",
                color: full || uploading ? "#4B5563" : "#9CA3AF",
              }}
            >
              {uploading ? t("adding") : t("add")}
              <input
                ref={fileRef}
                type="file"
                accept="image/*"
                disabled={full || uploading}
                style={{ display: "none" }}
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file) void handleFile(file);
                }}
              />
            </label>
            <span style={{ color: full ? "#F59E0B" : "#6B7280", fontSize: 12 }}>
              {t("counter", { count: images.length, max: MAX_IMAGES })}
              {full ? ` — ${t("maxReached")}` : ""}
            </span>
          </div>
        </div>
      )}

      {error && (
        <div style={{ background: "#2D0F0F", border: "1px solid #7F1D1D", borderRadius: 8, color: "#F87171", fontSize: 12, padding: "8px 12px" }}>
          {error}
        </div>
      )}
    </div>
  );
}
