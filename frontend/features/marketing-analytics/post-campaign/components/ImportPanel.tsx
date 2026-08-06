"use client";

import { useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { UploadCloud, FileText, X, Loader2 } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { usePostCampaignStore } from "../store/usePostCampaignStore";
import { useImportPostCampaignSegment } from "../hooks/usePostCampaign";
import { ColumnPreviewPicker } from "./ColumnPreviewPicker";
import type { PostCampaignImportMode } from "../types";

const MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // mirrors PostCampaignController.MaxImportFileSizeBytes
const ACCEPTED_EXTENSIONS = [".csv", ".xlsx", ".txt"];

const textareaStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 8,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "10px 12px",
  outline: "none",
  width: "100%",
  minHeight: 120,
  fontFamily: "monospace",
  resize: "vertical",
};
const inputStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 6,
  color: "#E8EDF5",
  fontSize: 13,
  padding: "8px 10px",
  outline: "none",
  width: "100%",
  maxWidth: 320,
};

function modeBtnStyle(active: boolean): React.CSSProperties {
  return {
    padding: "6px 14px",
    fontSize: 12,
    fontWeight: 600,
    borderRadius: 6,
    border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
    cursor: "pointer",
    background: active ? "#1D3461" : "#0D1117",
    color: active ? "#93C5FD" : "#6B7280",
  };
}

function errorMessage(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}

function hasValidExtension(fileName: string): boolean {
  const lower = fileName.toLowerCase();
  return ACCEPTED_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

/**
 * Import panel — "Список ID" / "Файл" toggle + textarea or dropzone + name + submit (source doc
 * §4/§5/§6). Unlike the competitor, there is NO live client-side parse-as-you-type preview: this
 * app's strict token parser (`SegmentImportParser`, brief's §5.3/§5.4 critical fix over the
 * competitor's broken digit-extraction parser) lives ONLY server-side, on purpose — duplicating it
 * client-side would risk the two silently drifting apart. So "Розпізнано N ID" only appears AFTER
 * a real import call returns (rendered by `ValidationSummary`, mounted by page.tsx once
 * `importResult` is set) — the button below is the actual trigger, not a preview step.
 *
 * Self-contained: owns its own import mutation (same pattern as `audience-builder/components/
 * BuyersTab/ExportReceiptsButton.tsx` owning its own export mutation) and writes the result
 * straight to `usePostCampaignStore` — page.tsx mounts this with no props.
 */
export function ImportPanel() {
  const t = useTranslations("Dashboard.postCampaign.importPanel");

  const inputMode = usePostCampaignStore((s) => s.inputMode);
  const rawText = usePostCampaignStore((s) => s.rawText);
  const file = usePostCampaignStore((s) => s.file);
  const columnIndex = usePostCampaignStore((s) => s.columnIndex);
  const importResult = usePostCampaignStore((s) => s.importResult);
  const setInputMode = usePostCampaignStore((s) => s.setInputMode);
  const setRawText = usePostCampaignStore((s) => s.setRawText);
  const setFile = usePostCampaignStore((s) => s.setFile);
  const setColumnIndex = usePostCampaignStore((s) => s.setColumnIndex);
  const applyImportResult = usePostCampaignStore((s) => s.applyImportResult);

  const [name, setName] = useState("");
  const [dragOver, setDragOver] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);
  const [pendingColumnIndex, setPendingColumnIndex] = useState<number | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const { mutate, isPending, error } = useImportPostCampaignSegment();

  function handleModeChange(m: PostCampaignImportMode) {
    setInputMode(m);
    setLocalError(null);
  }

  function pickFile(f: File) {
    setLocalError(null);
    if (!hasValidExtension(f.name)) {
      setLocalError(t("errorExtension"));
      return;
    }
    if (f.size > MAX_FILE_SIZE_BYTES) {
      setLocalError(t("errorSize"));
      return;
    }
    setFile(f);
  }

  function handleDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setDragOver(false);
    const dropped = e.dataTransfer.files?.[0];
    if (dropped) pickFile(dropped);
  }

  function submit(explicitColumnIndex?: number) {
    setLocalError(null);
    mutate(
      {
        rawText: inputMode === "list" ? rawText : undefined,
        file: inputMode === "file" ? (file ?? undefined) : undefined,
        name: name.trim() || undefined,
        columnIndex: explicitColumnIndex,
      },
      {
        onSuccess: (result) => {
          applyImportResult(result);
          // Reset the LOCAL override on every fresh result — otherwise a stale choice from a
          // previous file/result would leak into this one's picker (e.g. picking column 2 on file
          // A, then choosing an entirely different file B: without this reset the picker would
          // pre-select column 2 again for B's own, unrelated columns instead of B's own detected
          // default) and could spuriously show the "re-import" button as active.
          setPendingColumnIndex(null);
          if (result.columnPreview) {
            setColumnIndex(result.columnPreview.detectedColumnIndex);
          }
        },
      },
    );
  }

  function handleConfirmColumn() {
    if (pendingColumnIndex === null) return;
    setColumnIndex(pendingColumnIndex);
    submit(pendingColumnIndex);
  }

  const canSubmit = inputMode === "list" ? rawText.trim().length > 0 : file !== null;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
      <div style={{ display: "flex", gap: 6 }}>
        <button type="button" style={modeBtnStyle(inputMode === "list")} onClick={() => handleModeChange("list")}>
          {t("modeList")}
        </button>
        <button type="button" style={modeBtnStyle(inputMode === "file")} onClick={() => handleModeChange("file")}>
          {t("modeFile")}
        </button>
      </div>

      {inputMode === "list" ? (
        <div>
          <textarea
            value={rawText}
            onChange={(e) => setRawText(e.target.value)}
            placeholder={t("listPlaceholder")}
            style={textareaStyle}
          />
          <p style={{ color: "#4B5563", fontSize: 11.5, margin: "6px 0 0", lineHeight: 1.5 }}>{t("listHint")}</p>
        </div>
      ) : (
        <div>
          <div
            onDragOver={(e) => {
              e.preventDefault();
              setDragOver(true);
            }}
            onDragLeave={() => setDragOver(false)}
            onDrop={handleDrop}
            onClick={() => fileInputRef.current?.click()}
            style={{
              border: `1.5px dashed ${dragOver ? "#3B82F6" : "#1F2937"}`,
              borderRadius: 10,
              padding: "28px 16px",
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              justifyContent: "center",
              gap: 8,
              cursor: "pointer",
              background: dragOver ? "#0F1F3D" : "#0D1117",
              transition: "border-color 0.15s, background 0.15s",
            }}
          >
            <input
              ref={fileInputRef}
              type="file"
              accept={ACCEPTED_EXTENSIONS.join(",")}
              style={{ display: "none" }}
              onChange={(e) => {
                const picked = e.target.files?.[0];
                if (picked) pickFile(picked);
                e.target.value = "";
              }}
            />
            {file ? (
              <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
                <FileText size={18} color="#60A5FA" />
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>{file.name}</span>
                <button
                  type="button"
                  aria-label={t("removeFile")}
                  onClick={(e) => {
                    e.stopPropagation();
                    setFile(null);
                  }}
                  style={{ display: "flex", background: "transparent", border: "none", color: "#6B7280", cursor: "pointer", padding: 2 }}
                >
                  <X size={14} />
                </button>
              </div>
            ) : (
              <>
                <UploadCloud size={24} color="#4B5563" strokeWidth={1.5} />
                <span style={{ color: "#9CA3AF", fontSize: 13 }}>{t("dropHint")}</span>
              </>
            )}
          </div>
          <p style={{ color: "#4B5563", fontSize: 11.5, margin: "6px 0 0", lineHeight: 1.5 }}>{t("fileHint")}</p>
        </div>
      )}

      <div>
        <label style={{ display: "block", color: "#4B5563", fontSize: 12, marginBottom: 4 }}>{t("nameLabel")}</label>
        <input value={name} onChange={(e) => setName(e.target.value)} placeholder={t("namePlaceholder")} style={inputStyle} />
      </div>

      {(localError || error) && <div style={{ color: "#F87171", fontSize: 12.5 }}>{localError ?? errorMessage(error)}</div>}

      <div>
        <Btn
          variant="primary"
          disabled={!canSubmit || isPending}
          icon={isPending ? <Loader2 size={13} className="animate-spin" /> : undefined}
          onClick={() => submit(columnIndex ?? undefined)}
        >
          {isPending ? t("importing") : t("importButton")}
        </Btn>
      </div>

      {importResult?.columnPreview && (
        <ColumnPreviewPicker
          preview={importResult.columnPreview}
          selectedColumnIndex={pendingColumnIndex ?? columnIndex ?? importResult.columnPreview.detectedColumnIndex}
          onSelectColumn={setPendingColumnIndex}
          onConfirm={handleConfirmColumn}
          isPending={isPending}
        />
      )}
    </div>
  );
}
