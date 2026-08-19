"use client";

import { useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { History, RotateCcw, UploadCloud } from "lucide-react";
import { Btn } from "@/components/ui/Btn";
import { useUsers } from "@/features/users/hooks/useUsers";
import { extractDraftValidationErrors } from "../api/mobileConfigDraft";
import {
  useMobileConfigVersions,
  usePublishMobileConfigDraft,
  useRollbackMobileConfigVersion,
} from "../hooks/useMobileConfigVersions";
import { ConfirmDialog } from "./ConfirmDialog";
import type { MobileConfigVersionStatus, MobileConfigVersionSummary } from "../types";

const cardStyle: React.CSSProperties = {
  background: "#0D1117",
  border: "1px solid #1F2937",
  borderRadius: 12,
  padding: 24,
};

const rowStyle: React.CSSProperties = {
  border: "1px solid #1F2937",
  borderRadius: 10,
  padding: "12px 14px",
  display: "flex",
  alignItems: "center",
  justifyContent: "space-between",
  gap: 12,
  flexWrap: "wrap",
};

const STATUS_STYLE: Record<MobileConfigVersionStatus, { color: string; background: string; border: string }> = {
  draft: { color: "#9CA3AF", background: "#1F2937", border: "#374151" },
  published: { color: "#4ADE80", background: "#0F2D1A", border: "#166534" },
  archived: { color: "#6B7280", background: "#161B26", border: "#1F2937" },
};

function formatDateTime(value: string | null): string {
  if (!value) return "—";
  return new Date(value).toLocaleString();
}

type ConfirmAction = { type: "publish" } | { type: "rollback"; version: MobileConfigVersionSummary };

/**
 * TASK-546: Version History screen — fills the `/consumer-app/versions` placeholder TASK-535
 * scaffolded. Fetches the tenant's whole `GET /api/v1/mobile/config/versions` list (TASK-545 —
 * every publish/rollback creates a new row, none are ever deleted) and renders it newest-first.
 *
 * Rollback only renders for `"archived"` rows — `"published"` is already current and `"draft"` is
 * the tenant's in-progress edit; the backend rejects a rollback targeting either
 * (`CannotRollbackToCurrentVersion`), so the action is never offered for them here either (see
 * `MobileConfigVersionSummary`'s remarks in ../types.ts).
 *
 * "Publish draft" is the first in-app trigger for `POST /api/v1/mobile/config/publish` (TASK-544)
 * anywhere in Retailer Admin — every editor screen's draftNotice previously said publishing wasn't
 * available yet (TASK-544b). Both Publish and Rollback are one-way, consumer-visible actions, so
 * both go through the same `ConfirmDialog` (TASK-546's new confirmation-dialog pattern — see that
 * file's remarks) rather than a bare `window.confirm()`.
 */
export function VersionHistorySection() {
  const t = useTranslations("Dashboard.consumerApp.versions");
  const { data: versions, isLoading, isError } = useMobileConfigVersions();
  const { data: users } = useUsers();
  const publish = usePublishMobileConfigDraft();
  const rollback = useRollbackMobileConfigVersion();

  const [confirmAction, setConfirmAction] = useState<ConfirmAction | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  // Best-effort Guid → name resolution for `createdBy` — the history endpoint deliberately
  // doesn't join a display name server-side (see MobileConfigVersionSummaryDto's remarks), so this
  // maps against the tenant's existing user list (already fetched elsewhere in Retailer Admin, no
  // new backend surface needed). Falls back to a generic label when the id is null (JWT had no
  // resolvable actor) or doesn't match any current user (e.g. a deactivated/removed account).
  const userNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const u of users ?? []) map.set(u.id, u.fullName);
    return map;
  }, [users]);

  const pending = confirmAction?.type === "publish" ? publish.isPending : rollback.isPending;

  function openPublishConfirm() {
    setActionError(null);
    setConfirmAction({ type: "publish" });
  }

  function openRollbackConfirm(version: MobileConfigVersionSummary) {
    setActionError(null);
    setConfirmAction({ type: "rollback", version });
  }

  async function handleConfirm() {
    if (!confirmAction) return;
    setActionError(null);
    try {
      if (confirmAction.type === "publish") {
        await publish.mutateAsync();
        toast.success(t("publishSuccess"));
      } else {
        await rollback.mutateAsync(confirmAction.version.id);
        toast.success(t("rollbackSuccess", { version: confirmAction.version.version }));
      }
      setConfirmAction(null);
    } catch (err) {
      // Matches AppBuilderCanvas.tsx's `handleSave` convention: structured `{ field, message }`
      // errors are joined into one readable line (no per-field inputs to attach them to here,
      // unlike a form screen), anything else falls back to the thrown message.
      const fieldErrors = extractDraftValidationErrors(err);
      if (fieldErrors) {
        setActionError(fieldErrors.map((e) => `${e.field}: ${e.message}`).join(" "));
      } else {
        setActionError(err instanceof Error ? err.message : t("actionError"));
      }
    }
  }

  return (
    <div style={cardStyle}>
      <div
        style={{
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          marginBottom: 20,
          flexWrap: "wrap",
          gap: 12,
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
          <History size={22} style={{ color: "#9CA3AF" }} />
          <div>
            <h2 style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 700, margin: 0 }}>{t("listTitle")}</h2>
            <p style={{ color: "#4B5563", fontSize: 12, margin: 0, marginTop: 3 }}>{t("listSubtitle")}</p>
          </div>
        </div>
        <Btn size="sm" icon={<UploadCloud size={14} />} onClick={openPublishConfirm}>
          {t("publishButton")}
        </Btn>
      </div>

      {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}
      {isError && <div style={{ color: "#F87171", fontSize: 13 }}>{t("loadError")}</div>}

      {!isLoading && !isError && (versions?.length ?? 0) === 0 && (
        <div style={{ color: "#4B5563", fontSize: 13 }}>{t("emptyHint")}</div>
      )}

      <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
        {(versions ?? []).map((version) => {
          const style = STATUS_STYLE[version.status];
          const creator = version.createdBy ? userNameById.get(version.createdBy) : undefined;

          return (
            <div key={version.id} style={rowStyle}>
              <div style={{ display: "flex", alignItems: "center", gap: 10, minWidth: 0 }}>
                <span
                  style={{
                    fontSize: 10,
                    fontWeight: 700,
                    textTransform: "uppercase",
                    letterSpacing: "0.05em",
                    padding: "3px 8px",
                    borderRadius: 999,
                    color: style.color,
                    background: style.background,
                    border: `1px solid ${style.border}`,
                    flexShrink: 0,
                  }}
                >
                  {t(`status.${version.status}`)}
                </span>
                <span style={{ color: "#E8EDF5", fontSize: 13, fontWeight: 600 }}>
                  {t("versionLabel", { version: version.version })}
                </span>
              </div>

              <div
                style={{
                  display: "flex",
                  gap: 16,
                  flexWrap: "wrap",
                  color: "#6B7280",
                  fontSize: 12,
                  flex: 1,
                  minWidth: 240,
                }}
              >
                <span>
                  {t("createdAtLabel")} {formatDateTime(version.createdAt)}
                </span>
                {version.publishedAt && (
                  <span>
                    {t("publishedAtLabel")} {formatDateTime(version.publishedAt)}
                  </span>
                )}
                <span>
                  {t("createdByLabel")} {creator ?? t("unknownCreator")}
                </span>
              </div>

              {version.status === "archived" && (
                <Btn
                  size="sm"
                  variant="ghost"
                  icon={<RotateCcw size={13} />}
                  onClick={() => openRollbackConfirm(version)}
                >
                  {t("rollbackButton")}
                </Btn>
              )}
            </div>
          );
        })}
      </div>

      {confirmAction && (
        <ConfirmDialog
          title={
            confirmAction.type === "publish"
              ? t("publishConfirmTitle")
              : t("rollbackConfirmTitle", { version: confirmAction.version.version })
          }
          description={
            confirmAction.type === "publish"
              ? t("publishConfirmDescription")
              : t("rollbackConfirmDescription", { version: confirmAction.version.version })
          }
          confirmLabel={pending ? t("confirmPending") : t("confirmButton")}
          cancelLabel={t("cancelButton")}
          variant={confirmAction.type === "publish" ? "primary" : "danger"}
          pending={pending}
          error={actionError}
          onConfirm={handleConfirm}
          onClose={() => {
            if (pending) return;
            setConfirmAction(null);
            setActionError(null);
          }}
        />
      )}
    </div>
  );
}
