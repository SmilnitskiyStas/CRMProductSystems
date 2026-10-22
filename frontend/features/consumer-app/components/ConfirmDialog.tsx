"use client";

import { Btn } from "@/components/ui/Btn";
import { Modal } from "@/components/ui/Modal";

interface ConfirmDialogProps {
  title: string;
  description: string;
  confirmLabel: string;
  cancelLabel: string;
  /** "primary" for a routine confirm (Publish), "danger" for a consequential one (Rollback). */
  variant?: "primary" | "danger" | "success";
  pending?: boolean;
  /** Shown inside the dialog when the last confirm attempt failed — kept in the dialog itself
   *  (not the page behind it, which this overlay covers) so the failure is visible where the
   *  user is looking. */
  error?: string | null;
  onConfirm: () => void;
  onClose: () => void;
}

/**
 * TASK-546: first confirmation-dialog component in this codebase's inline-styled feature areas.
 * `frontend/components/ui/alert-dialog.tsx` (shadcn primitive) exists but nothing uses it, and
 * this feature area (ThemeEditorSection/AppBuilderCanvas/NavigationBuilderSection/BannersSection)
 * has no Tailwind/shadcn form usage of its own — every dialog here is a hand-styled overlay
 * (`Modal.tsx`, `ReasonModal.tsx`). This mirrors that exact visual pattern rather than introducing
 * a second design language for one screen. Used by `VersionHistorySection` for both Publish and
 * Rollback — both are one-way, consumer-visible actions (DoD requires an explicit confirmation
 * step for Publish; Rollback republishes immediately, same consequence class, so it reuses this
 * rather than a second bespoke dialog).
 */
export function ConfirmDialog({
  title,
  description,
  confirmLabel,
  cancelLabel,
  variant = "primary",
  pending = false,
  error,
  onConfirm,
  onClose,
}: ConfirmDialogProps) {
  return (
    <Modal
      title={title}
      width={440}
      onClose={() => {
        if (!pending) onClose();
      }}
    >
      <p style={{ color: "#9CA3AF", fontSize: 13, margin: 0, lineHeight: 1.5 }}>{description}</p>

      {error && (
        <p style={{ color: "#F87171", fontSize: 12, marginTop: 12, lineHeight: 1.5 }}>{error}</p>
      )}

      <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 20 }}>
        <Btn variant="ghost" onClick={onClose} disabled={pending}>
          {cancelLabel}
        </Btn>
        <Btn variant={variant} disabled={pending} onClick={onConfirm}>
          {confirmLabel}
        </Btn>
      </div>
    </Modal>
  );
}
