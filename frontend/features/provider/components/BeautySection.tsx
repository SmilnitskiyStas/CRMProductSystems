"use client";

import { useEffect, useId, useRef, useState } from "react";
import type { CSSProperties } from "react";
import { useTranslations, useLocale } from "next-intl";
import { Copy, Check } from "lucide-react";
import { ApiError } from "@/lib/api";
import { Btn } from "@/components/ui/Btn";
import { BeautyDialog } from "./BeautyDialog";
import {
  useBeautyOwnerInvite,
  useBeautySetSuspended,
  useBeautyTenant,
  useBeautyUpdateModules,
  useCreateBeautyTenant,
} from "../hooks/useProviderBeauty";
import { BEAUTY_DEFAULT_MODULES, BEAUTY_MODULES } from "../types";

const SLUG_RE = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
export const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

const inputStyle: CSSProperties = {
  width: "100%",
  background: "#111827",
  border: "1px solid #1F2937",
  borderRadius: 8,
  padding: "9px 12px",
  color: "#E8EDF5",
  fontSize: 13,
  outline: "none",
  boxSizing: "border-box",
};
const labelStyle: CSSProperties = { display: "block", color: "#9CA3AF", fontSize: 12, fontWeight: 500, marginBottom: 6 };
const errStyle: CSSProperties = { color: "#F87171", fontSize: 11, marginTop: 4 };

/** One-time secret returned by create / owner-invite. Lives only in the open dialog's state. */
export interface Invite {
  url: string;
  expiresAt: string;
}

/** Maps a control-plane error `{code}` to an i18n key under `errors.*`. */
export function beautyErrorKey(err: unknown): "slugTaken" | "externalRefConflict" | "unavailable" | "validation" | "tenantSuspended" | "generic" {
  const code = err instanceof ApiError ? (err.body as { code?: string } | undefined)?.code : undefined;
  switch (code) {
    case "slug_taken":
    case "tenant_slug_taken":
      return "slugTaken";
    case "external_ref_conflict":
      return "externalRefConflict";
    case "beauty_unavailable":
      return "unavailable";
    case "validation":
      return "validation";
    case "tenant_suspended":
      return "tenantSuspended";
    default:
      return err instanceof ApiError && err.status === 503 ? "unavailable" : "generic";
  }
}

function ErrorBox({ children }: { children: React.ReactNode }) {
  return (
    <div
      role="alert"
      style={{ color: "#F87171", fontSize: 13, background: "#1F1211", border: "1px solid #7F1D1D", borderRadius: 8, padding: "10px 14px" }}
    >
      {children}
    </div>
  );
}

// ── Invite link (shown once) ─────────────────────────────────────────────────

export function BeautyInviteDialog({ invite, onClose }: { invite: Invite; onClose: () => void }) {
  const t = useTranslations("Dashboard.provider.beauty");
  const locale = useLocale();
  const [copied, setCopied] = useState(false);
  const [copyFailed, setCopyFailed] = useState(false);
  const inputId = useId();
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => () => {
    if (copiedTimer.current) clearTimeout(copiedTimer.current);
  }, []);

  async function copy() {
    try {
      await navigator.clipboard.writeText(invite.url);
      setCopied(true);
      setCopyFailed(false);
      if (copiedTimer.current) clearTimeout(copiedTimer.current);
      copiedTimer.current = setTimeout(() => setCopied(false), 2500);
    } catch {
      setCopied(false);
      setCopyFailed(true);
    }
  }

  const expires = new Date(invite.expiresAt).toLocaleString(locale === "en" ? "en-US" : "uk-UA", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
  });

  return (
    <BeautyDialog title={t("inviteTitle")} onClose={onClose} closeLabel={t("close")} width={520}>
      <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        <div
          role="note"
          style={{ color: "#FCD34D", fontSize: 13, background: "#451A03", border: "1px solid #92400E", borderRadius: 8, padding: "10px 14px" }}
        >
          {t("inviteWarning")}
        </div>
        <div>
          <label htmlFor={inputId} style={labelStyle}>{t("inviteLinkLabel")}</label>
          <div style={{ display: "flex", gap: 8 }}>
            <input
              id={inputId}
              autoFocus
              readOnly
              value={invite.url}
              onFocus={(e) => e.currentTarget.select()}
              spellCheck={false}
              autoComplete="off"
              style={{ ...inputStyle, fontFamily: "monospace", fontSize: 12 }}
            />
            <Btn type="button" onClick={copy} icon={copied ? <Check size={14} /> : <Copy size={14} />}>
              {copied ? t("copied") : t("copy")}
            </Btn>
          </div>
          <div aria-live="polite" style={{ minHeight: 16, marginTop: 4 }}>
            {copied && <span style={{ color: "#4ADE80", fontSize: 11 }}>{t("copied")}</span>}
            {copyFailed && <span style={errStyle}>{t("copyFailed")}</span>}
          </div>
        </div>
        <div style={{ color: "#9CA3AF", fontSize: 12 }}>{t("inviteExpires", { date: expires })}</div>
        <Btn type="button" variant="ghost" onClick={onClose} style={{ justifyContent: "center" }}>
          {t("close")}
        </Btn>
      </div>
    </BeautyDialog>
  );
}

// ── Modules checkboxes ───────────────────────────────────────────────────────

export function ModuleChecklist({ selected, onToggle, disabled }: { selected: string[]; onToggle: (m: string) => void; disabled?: boolean }) {
  const t = useTranslations("Dashboard.provider.beauty");
  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
      {BEAUTY_MODULES.map((m) => {
        const active = selected.includes(m);
        return (
          <label
            key={m}
            style={{
              display: "flex", alignItems: "center", gap: 10, padding: "8px 12px", borderRadius: 7,
              background: active ? "#0F1F3D" : "#111827",
              border: `1px solid ${active ? "#3B82F6" : "#1F2937"}`,
              cursor: disabled ? "default" : "pointer",
            }}
          >
            <input
              type="checkbox"
              checked={active}
              disabled={disabled}
              onChange={() => onToggle(m)}
              style={{ accentColor: "#3B82F6", width: 14, height: 14 }}
            />
            <span style={{ color: active ? "#93C5FD" : "#9CA3AF", fontSize: 13 }}>{t(`modules.${m}`)}</span>
          </label>
        );
      })}
    </div>
  );
}

function toggleIn(list: string[], m: string) {
  return list.includes(m) ? list.filter((x) => x !== m) : [...list, m];
}

// ── Create dialog ────────────────────────────────────────────────────────────

export function CreateBeautyDialog({
  tenantId, defaultName, defaultSlug, onClose, onCreated,
}: {
  tenantId: string;
  defaultName: string;
  defaultSlug: string;
  onClose: () => void;
  onCreated: (invite: Invite | null) => void;
}) {
  const t = useTranslations("Dashboard.provider.beauty");
  const create = useCreateBeautyTenant();
  const [name, setName] = useState(defaultName);
  const [slug, setSlug] = useState(defaultSlug.toLowerCase().replace(/[^a-z0-9-]+/g, "-").replace(/^-+|-+$/g, ""));
  const [ownerEmail, setOwnerEmail] = useState("");
  const [ownerName, setOwnerName] = useState("");
  const [modules, setModules] = useState<string[]>(BEAUTY_DEFAULT_MODULES);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [serverError, setServerError] = useState("");
  const ids = { name: useId(), slug: useId(), email: useId(), owner: useId() };

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setServerError("");
    const errs: Record<string, string> = {};
    if (!name.trim()) errs.name = t("errors.nameRequired");
    if (slug.length < 2 || slug.length > 63 || !SLUG_RE.test(slug)) errs.slug = t("errors.slugInvalid");
    if (!EMAIL_RE.test(ownerEmail.trim())) errs.email = t("errors.emailInvalid");
    if (modules.length === 0) errs.modules = t("errors.modulesRequired");
    setErrors(errs);
    if (Object.keys(errs).length > 0) return;

    try {
      const res = await create.mutateAsync({
        shelfGuardTenantId: tenantId,
        name: name.trim(),
        slug,
        ownerEmail: ownerEmail.trim(),
        ownerName: ownerName.trim() || undefined,
        modules,
      });
      create.reset(); // drop the one-time inviteUrl from the mutation cache
      onCreated(res.inviteUrl && res.inviteExpiresAt ? { url: res.inviteUrl, expiresAt: res.inviteExpiresAt } : null);
    } catch (err) {
      setServerError(t(`errors.${beautyErrorKey(err)}`));
    }
  }

  const field = (id: string, label: string, value: string, set: (v: string) => void, error?: string, extra?: Partial<React.InputHTMLAttributes<HTMLInputElement>>) => (
    <div>
      <label htmlFor={id} style={labelStyle}>{label}</label>
      <input
        id={id}
        value={value}
        onChange={(e) => set(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${id}-err` : undefined}
        style={{ ...inputStyle, borderColor: error ? "#7F1D1D" : "#1F2937" }}
        {...extra}
      />
      {error && <div id={`${id}-err`} style={errStyle}>{error}</div>}
    </div>
  );

  return (
    <BeautyDialog title={t("createTitle")} onClose={onClose} closeLabel={t("close")} busy={create.isPending}>
      <form onSubmit={submit} noValidate style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        {serverError && <ErrorBox>{serverError}</ErrorBox>}
        {field(ids.name, t("fieldName"), name, setName, errors.name, { autoFocus: true })}
        {field(ids.slug, t("fieldSlug"), slug, (v) => setSlug(v.toLowerCase()), errors.slug, { spellCheck: false, autoComplete: "off" })}
        {field(ids.email, t("fieldOwnerEmail"), ownerEmail, setOwnerEmail, errors.email, { type: "email", autoComplete: "off" })}
        {field(ids.owner, t("fieldOwnerName"), ownerName, setOwnerName, undefined, { autoComplete: "off" })}
        <fieldset style={{ border: "none", padding: 0, margin: 0 }}>
          <legend style={labelStyle}>{t("fieldModules")}</legend>
          <ModuleChecklist selected={modules} onToggle={(m) => setModules((p) => toggleIn(p, m))} />
          {errors.modules && <div role="alert" style={errStyle}>{errors.modules}</div>}
        </fieldset>
        <div style={{ display: "flex", gap: 10 }}>
          <Btn type="submit" disabled={create.isPending} style={{ flex: 1, justifyContent: "center" }}>
            {create.isPending ? t("creating") : t("createSubmit")}
          </Btn>
          <Btn type="button" variant="ghost" onClick={onClose} disabled={create.isPending}>
            {t("cancel")}
          </Btn>
        </div>
      </form>
    </BeautyDialog>
  );
}

// ── Modules dialog ───────────────────────────────────────────────────────────

export function BeautyModulesDialog({ tenantId, current, onClose }: { tenantId: string; current: string[]; onClose: () => void }) {
  const t = useTranslations("Dashboard.provider.beauty");
  const update = useBeautyUpdateModules(tenantId);
  const [modules, setModules] = useState<string[]>(current);
  const [error, setError] = useState("");

  async function save() {
    if (modules.length === 0) {
      setError(t("errors.modulesRequired"));
      return;
    }
    setError("");
    try {
      await update.mutateAsync(modules);
      onClose();
    } catch (err) {
      setError(t(`errors.${beautyErrorKey(err)}`));
    }
  }

  return (
    <BeautyDialog title={t("modulesTitle")} onClose={onClose} closeLabel={t("close")} busy={update.isPending}>
      <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        {error && <ErrorBox>{error}</ErrorBox>}
        <ModuleChecklist selected={modules} onToggle={(m) => setModules((p) => toggleIn(p, m))} disabled={update.isPending} />
        <div style={{ display: "flex", gap: 10 }}>
          <Btn type="button" onClick={save} disabled={update.isPending} autoFocus style={{ flex: 1, justifyContent: "center" }}>
            {update.isPending ? t("saving") : t("save")}
          </Btn>
          <Btn type="button" variant="ghost" onClick={onClose} disabled={update.isPending}>
            {t("cancel")}
          </Btn>
        </div>
      </div>
    </BeautyDialog>
  );
}

// ── Confirm (suspend / resume) ───────────────────────────────────────────────

export function BeautyConfirmDialog({
  title, message, confirmLabel, danger, busy, error, onConfirm, onClose,
}: {
  title: string;
  message: string;
  confirmLabel: string;
  danger?: boolean;
  busy: boolean;
  error: string;
  onConfirm: () => void;
  onClose: () => void;
}) {
  const t = useTranslations("Dashboard.provider.beauty");
  return (
    <BeautyDialog title={title} onClose={onClose} closeLabel={t("close")} busy={busy} role="alertdialog" width={420}>
      <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        {error && <ErrorBox>{error}</ErrorBox>}
        <p style={{ color: "#D1D5DB", fontSize: 13, margin: 0 }}>{message}</p>
        <div style={{ display: "flex", gap: 10 }}>
          <Btn type="button" variant={danger ? "danger" : "success"} onClick={onConfirm} disabled={busy} style={{ flex: 1, justifyContent: "center" }}>
            {busy ? t("saving") : confirmLabel}
          </Btn>
          <Btn type="button" variant="ghost" onClick={onClose} disabled={busy} autoFocus>
            {t("cancel")}
          </Btn>
        </div>
      </div>
    </BeautyDialog>
  );
}

// ── Section ──────────────────────────────────────────────────────────────────

type Dlg = "create" | "modules" | "suspend" | "resume" | null;

export function BeautySection({ tenantId, tenantName, tenantSlug }: { tenantId: string; tenantName: string; tenantSlug: string }) {
  const t = useTranslations("Dashboard.provider.beauty");
  const { data, isLoading, isError } = useBeautyTenant(tenantId);
  const setSuspended = useBeautySetSuspended(tenantId);
  const ownerInvite = useBeautyOwnerInvite(tenantId);
  const [dialog, setDialog] = useState<Dlg>(null);
  const [invite, setInvite] = useState<Invite | null>(null);
  const [noInvite, setNoInvite] = useState(false);
  const [actionError, setActionError] = useState("");

  const exists = data?.exists === true;
  const suspended = data?.status === "suspended";

  function closeDialog() {
    setDialog(null);
    setActionError("");
  }

  async function confirmSuspend(suspend: boolean) {
    setActionError("");
    try {
      await setSuspended.mutateAsync(suspend);
      setDialog(null);
    } catch (err) {
      setActionError(t(`errors.${beautyErrorKey(err)}`));
    }
  }

  function showInvite(inv: Invite | null) {
    if (inv) setInvite(inv);
    else setNoInvite(true);
  }

  async function reissueInvite() {
    setActionError("");
    try {
      const res = await ownerInvite.mutateAsync();
      ownerInvite.reset();
      showInvite(res.inviteUrl && res.expiresAt ? { url: res.inviteUrl, expiresAt: res.expiresAt } : null);
    } catch (err) {
      setActionError(t(`errors.${beautyErrorKey(err)}`));
    }
  }

  const statusLabel = !exists ? t("statusNone") : suspended ? t("statusSuspended") : t("statusActive");
  const statusColors = !exists
    ? { bg: "#1c1917", border: "#374151", color: "#6B7280" }
    : suspended
      ? { bg: "#451A03", border: "#92400E", color: "#FCD34D" }
      : { bg: "#052e16", border: "#166534", color: "#4ADE80" };

  return (
    <section
      aria-label={t("sectionTitle")}
      style={{ background: "#0D1117", border: "1px solid #1F2937", borderRadius: 10, padding: "14px 16px" }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 10 }}>
        <h3 style={{ color: "#9CA3AF", fontSize: 12, fontWeight: 600, margin: 0 }}>{t("sectionTitle")}</h3>
        {!isLoading && !isError && (
          <span
            style={{
              padding: "2px 8px", borderRadius: 5, fontSize: 11, fontWeight: 600,
              background: statusColors.bg, border: `1px solid ${statusColors.border}`, color: statusColors.color,
            }}
          >
            {statusLabel}
          </span>
        )}
      </div>

      {isLoading && <div style={{ color: "#4B5563", fontSize: 13 }}>{t("loading")}</div>}
      {isError && <div role="alert" style={{ color: "#F87171", fontSize: 13 }}>{t("errors.unavailable")}</div>}

      {!isLoading && !isError && !exists && (
        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          <div style={{ color: "#9CA3AF", fontSize: 12 }}>{t("noneHint")}</div>
          <div>
            <Btn type="button" onClick={() => setDialog("create")}>{t("createButton")}</Btn>
          </div>
        </div>
      )}

      {!isLoading && !isError && exists && data && (
        <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ color: "#9CA3AF", fontSize: 12 }}>
            {t("slugLabel")}: <span style={{ color: "#E8EDF5", fontFamily: "monospace" }}>{data.slug}</span>
            {data.ownerState && (
              <span style={{ marginLeft: 12 }}>
                {t("ownerLabel")}: <span style={{ color: "#E8EDF5" }}>{t(`owner.${data.ownerState}`)}</span>
              </span>
            )}
          </div>
          <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
            {(data.modules ?? []).length === 0 ? (
              <span style={{ color: "#4B5563", fontSize: 12 }}>{t("noModules")}</span>
            ) : (
              (data.modules ?? []).map((m) => (
                <span key={m} style={{ padding: "3px 9px", borderRadius: 6, fontSize: 11, background: "#0F1F3D", border: "1px solid #1E3A5F", color: "#93C5FD" }}>
                  {t.has(`modules.${m}`) ? t(`modules.${m}`) : m}
                </span>
              ))
            )}
          </div>

          {actionError && !dialog && <ErrorBox>{actionError}</ErrorBox>}

          <div style={{ display: "flex", flexWrap: "wrap", gap: 8 }}>
            <Btn size="sm" variant="ghost" onClick={() => setDialog("modules")}>{t("modulesButton")}</Btn>
            <Btn
              size="sm"
              variant="ghost"
              onClick={reissueInvite}
              disabled={ownerInvite.isPending || suspended || data.ownerState === "active"}
            >
              {ownerInvite.isPending ? t("saving") : t("inviteButton")}
            </Btn>
            {suspended ? (
              <Btn size="sm" variant="success" onClick={() => setDialog("resume")}>{t("resumeButton")}</Btn>
            ) : (
              <Btn size="sm" variant="danger" onClick={() => setDialog("suspend")}>{t("suspendButton")}</Btn>
            )}
          </div>
          {(suspended || data.ownerState === "active") && (
            <div style={{ color: "#9CA3AF", fontSize: 11 }}>
              {suspended ? t("inviteDisabledSuspended") : t("inviteDisabledActive")}
            </div>
          )}
        </div>
      )}

      {dialog === "create" && (
        <CreateBeautyDialog
          tenantId={tenantId}
          defaultName={tenantName}
          defaultSlug={tenantSlug}
          onClose={closeDialog}
          onCreated={(inv) => {
            setDialog(null);
            showInvite(inv);
          }}
        />
      )}
      {dialog === "modules" && (
        <BeautyModulesDialog tenantId={tenantId} current={data?.modules ?? []} onClose={closeDialog} />
      )}
      {dialog === "suspend" && (
        <BeautyConfirmDialog
          title={t("suspendTitle")}
          message={t("suspendMessage")}
          confirmLabel={t("suspendConfirm")}
          danger
          busy={setSuspended.isPending}
          error={actionError}
          onConfirm={() => confirmSuspend(true)}
          onClose={closeDialog}
        />
      )}
      {dialog === "resume" && (
        <BeautyConfirmDialog
          title={t("resumeTitle")}
          message={t("resumeMessage")}
          confirmLabel={t("resumeConfirm")}
          busy={setSuspended.isPending}
          error={actionError}
          onConfirm={() => confirmSuspend(false)}
          onClose={closeDialog}
        />
      )}
      {invite && <BeautyInviteDialog invite={invite} onClose={() => setInvite(null)} />}
      {noInvite && (
        <BeautyDialog title={t("inviteTitle")} onClose={() => setNoInvite(false)} closeLabel={t("close")} width={480}>
          <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
            <div role="note" style={{ color: "#FCD34D", fontSize: 13, background: "#451A03", border: "1px solid #92400E", borderRadius: 8, padding: "10px 14px" }}>
              {t("inviteAlreadyIssued")}
            </div>
            <Btn type="button" variant="ghost" onClick={() => setNoInvite(false)} style={{ justifyContent: "center" }}>
              {t("close")}
            </Btn>
          </div>
        </BeautyDialog>
      )}
    </section>
  );
}
