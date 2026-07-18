"use client";

import { useState } from "react";
import { toast } from "sonner";
import { useTranslations, useLocale } from "next-intl";
import { MessageSquare, Plus, ChevronLeft, Building2, User, X, Send } from "lucide-react";
import {
  useProviderTickets,
  useCreateProviderTicket,
  useProviderTicket,
  useAddProviderComment,
} from "@/features/service-desk/hooks/useProviderTickets";
import { useTenants } from "@/features/provider/hooks/useProvider";
import {
  TICKET_STATUS_LABELS,
  TICKET_PRIORITY_LABELS,
  TICKET_CATEGORY_LABELS,
  type TicketStatus,
  type TicketCategory,
  type TicketPriority,
  type ProviderTicketListItemDto,
} from "@/features/service-desk/types";
import { TicketStatusBadge } from "@/features/service-desk/components/TicketStatusBadge";
import { PriorityBadge } from "@/features/service-desk/components/PriorityBadge";
import { Btn } from "@/components/ui/Btn";

// ── Helpers ──────────────────────────────────────────────────────────────────
// NOTE: TICKET_STATUS_LABELS / TICKET_PRIORITY_LABELS / TICKET_CATEGORY_LABELS and
// the Badge components come from features/service-desk (i18n rollout Block 10, not
// yet translated) — left as-is; only this file's own strings are translated here.

function formatDate(iso: string, locale: string): string {
  return new Date(iso).toLocaleDateString(locale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

// ── Ticket row ────────────────────────────────────────────────────────────────

function TicketRow({
  ticket,
  selected,
  onClick,
}: {
  ticket: ProviderTicketListItemDto;
  selected: boolean;
  onClick: () => void;
}) {
  const t = useTranslations("Dashboard.provider.supportTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  return (
    <div
      onClick={onClick}
      style={{
        background: selected ? "#0D1B2E" : "#0D1117",
        border: `1px solid ${selected ? "#3B82F6" : "#1F2937"}`,
        borderRadius: 10,
        padding: "14px 16px",
        cursor: "pointer",
        transition: "border-color 0.15s, background 0.15s",
      }}
      onMouseEnter={(e) => {
        if (!selected) {
          (e.currentTarget as HTMLElement).style.borderColor = "#374151";
          (e.currentTarget as HTMLElement).style.background = "#111827";
        }
      }}
      onMouseLeave={(e) => {
        if (!selected) {
          (e.currentTarget as HTMLElement).style.borderColor = "#1F2937";
          (e.currentTarget as HTMLElement).style.background = "#0D1117";
        }
      }}
    >
      {/* Top row: number + badges */}
      <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8, flexWrap: "wrap" }}>
        <span style={{ color: "#4B5563", fontSize: 12, fontWeight: 600 }}>
          #{ticket.number}
        </span>
        {/* Company badge */}
        <span
          style={{
            display: "flex",
            alignItems: "center",
            gap: 4,
            background: "#1D3461",
            border: "1px solid #2D4A7A",
            borderRadius: 5,
            padding: "1px 7px",
            fontSize: 11,
            color: "#93C5FD",
          }}
        >
          <Building2 size={10} />
          {ticket.tenantName}
        </span>
        {ticket.createdByProvider && (
          <span
            style={{
              background: "rgba(124,58,237,0.15)",
              border: "1px solid rgba(124,58,237,0.4)",
              borderRadius: 5,
              padding: "1px 7px",
              fontSize: 11,
              color: "#A78BFA",
            }}
          >
            {t("providerBadge")}
          </span>
        )}
        <span
          style={{
            background: "#1F2937",
            color: "#9CA3AF",
            borderRadius: 4,
            padding: "1px 6px",
            fontSize: 11,
          }}
        >
          {TICKET_CATEGORY_LABELS[ticket.category]}
        </span>
        <PriorityBadge priority={ticket.priority} />
        <TicketStatusBadge status={ticket.status} />
      </div>

      {/* Title */}
      <div
        style={{
          color: "#E8EDF5",
          fontSize: 14,
          fontWeight: 600,
          marginBottom: 8,
          lineHeight: 1.4,
        }}
      >
        {ticket.title}
      </div>

      {/* Meta */}
      <div style={{ display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
        <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#6B7280", fontSize: 12 }}>
          <User size={12} />
          {ticket.createdByName}
        </span>
        <span style={{ color: "#4B5563", fontSize: 12 }}>{formatDate(ticket.createdAt, intlLocale)}</span>
        {ticket.commentCount > 0 && (
          <span style={{ display: "flex", alignItems: "center", gap: 4, color: "#6B7280", fontSize: 12 }}>
            <MessageSquare size={12} />
            {ticket.commentCount}
          </span>
        )}
      </div>
    </div>
  );
}

// ── Detail panel ──────────────────────────────────────────────────────────────

function formatDateTime(iso: string, locale: string): string {
  return new Date(iso).toLocaleString(locale, {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function TicketDetailPanel({
  ticket,
  onBack,
}: {
  ticket: ProviderTicketListItemDto;
  onBack: () => void;
}) {
  const t = useTranslations("Dashboard.provider.supportTab");
  const locale = useLocale();
  const intlLocale = locale === "en" ? "en-US" : "uk-UA";
  const { data: detail } = useProviderTicket(ticket.id);
  const addComment = useAddProviderComment();
  const [commentBody, setCommentBody] = useState("");

  const comments = detail?.comments ?? [];

  function handleSend() {
    if (!commentBody.trim()) return;
    addComment.mutate(
      { id: ticket.id, data: { body: commentBody.trim(), isInternal: false } },
      {
        onSuccess: () => {
          toast.success(t("toastReplySent"));
          setCommentBody("");
        },
        onError: (err) => toast.error(err.message),
      }
    );
  }

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
        display: "flex",
        flexDirection: "column",
        gap: 16,
      }}
    >
      {/* Header */}
      <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
        <button
          onClick={onBack}
          style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer", padding: 4 }}
        >
          <ChevronLeft size={18} />
        </button>
        <div style={{ flex: 1 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 4 }}>
            <span style={{ color: "#4B5563", fontSize: 12 }}>#{ticket.number}</span>
            <span
              style={{
                display: "flex",
                alignItems: "center",
                gap: 4,
                background: "#1D3461",
                border: "1px solid #2D4A7A",
                borderRadius: 5,
                padding: "1px 7px",
                fontSize: 11,
                color: "#93C5FD",
              }}
            >
              <Building2 size={10} />
              {ticket.tenantName}
            </span>
          </div>
          <div style={{ color: "#E8EDF5", fontSize: 15, fontWeight: 600 }}>{ticket.title}</div>
        </div>
        <div style={{ display: "flex", gap: 6 }}>
          <PriorityBadge priority={ticket.priority} />
          <TicketStatusBadge status={ticket.status} />
        </div>
      </div>

      {/* Description */}
      <div
        style={{
          background: "#080C10",
          border: "1px solid #1F2937",
          borderRadius: 8,
          padding: "14px 16px",
          color: "#9CA3AF",
          fontSize: 13,
          lineHeight: 1.6,
          whiteSpace: "pre-wrap",
        }}
      >
        {ticket.description || <span style={{ color: "#374151" }}>{t("noDescription")}</span>}
      </div>

      {/* Meta grid */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "1fr 1fr",
          gap: "10px 20px",
          background: "#080C10",
          border: "1px solid #1F2937",
          borderRadius: 8,
          padding: "14px 16px",
        }}
      >
        {[
          [t("metaCategory"),  TICKET_CATEGORY_LABELS[ticket.category]],
          [t("metaStatus"),    TICKET_STATUS_LABELS[ticket.status]],
          [t("metaPriority"),  TICKET_PRIORITY_LABELS[ticket.priority]],
          [t("metaAuthor"),    ticket.createdByName],
          [t("metaCompany"),   ticket.tenantName],
          [t("metaCreated"),   formatDate(ticket.createdAt, intlLocale)],
          [t("metaComments"),  String(ticket.commentCount)],
          [t("metaInitiator"), ticket.createdByProvider ? t("providerBadge") : t("clientLabel")],
        ].map(([label, value]) => (
          <div key={label}>
            <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 2 }}>{label}</div>
            <div style={{ color: "#E8EDF5", fontSize: 13 }}>{value}</div>
          </div>
        ))}
      </div>

      {/* Comments */}
      <div>
        <div
          style={{
            display: "flex",
            alignItems: "center",
            gap: 8,
            marginBottom: 12,
            color: "#CBD5E1",
            fontSize: 14,
            fontWeight: 600,
          }}
        >
          <MessageSquare size={15} />
          {t("commentsTitle", { count: comments.length })}
        </div>

        {comments.length === 0 && (
          <div style={{ color: "#4B5563", fontSize: 13, marginBottom: 12 }}>
            {t("noComments")}
          </div>
        )}

        <div style={{ display: "flex", flexDirection: "column", gap: 8, marginBottom: 16 }}>
          {comments.map((c) => (
            <div
              key={c.id}
              style={{
                background: "#111827",
                border: "1px solid #1F2937",
                borderRadius: 8,
                padding: "10px 14px",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
                <span style={{ color: "#CBD5E1", fontSize: 13, fontWeight: 600 }}>
                  {c.authorName}
                </span>
                <span style={{ color: "#4B5563", fontSize: 11, marginLeft: "auto" }}>
                  {formatDateTime(c.createdAt, intlLocale)}
                </span>
              </div>
              <div style={{ color: "#9CA3AF", fontSize: 13, lineHeight: 1.5, whiteSpace: "pre-wrap" }}>
                {c.body}
              </div>
            </div>
          ))}
        </div>

        {/* Reply box */}
        <div
          style={{
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 8,
            padding: 12,
            display: "flex",
            flexDirection: "column",
            gap: 8,
          }}
        >
          <textarea
            value={commentBody}
            onChange={(e) => setCommentBody(e.target.value)}
            placeholder={t("replyPlaceholder")}
            rows={3}
            style={{
              background: "#0D1117",
              border: "1px solid #1F2937",
              borderRadius: 6,
              color: "#E8EDF5",
              fontSize: 13,
              padding: "8px 10px",
              outline: "none",
              resize: "vertical",
              width: "100%",
              boxSizing: "border-box",
              fontFamily: "inherit",
            }}
          />
          <Btn
            icon={<Send size={14} />}
            onClick={handleSend}
            disabled={!commentBody.trim() || addComment.isPending}
            style={{ marginLeft: "auto" }}
          >
            {addComment.isPending ? t("sending") : t("sendButton")}
          </Btn>
        </div>
      </div>
    </div>
  );
}

// ── Create ticket modal ───────────────────────────────────────────────────────

function CreateTicketModal({ onClose }: { onClose: () => void }) {
  const t = useTranslations("Dashboard.provider.supportTab");
  const { data: tenants } = useTenants();
  const create = useCreateProviderTicket();

  const [targetTenantId, setTargetTenantId] = useState("");
  const [title,          setTitle]          = useState("");
  const [description,    setDescription]    = useState("");
  const [category,       setCategory]       = useState<TicketCategory>("general");
  const [priority,       setPriority]       = useState<TicketPriority>("medium");
  const [error,          setError]          = useState<string | null>(null);

  const activeTenants = (tenants ?? []).filter((t) => t.isActive);
  const canSubmit = targetTenantId && title.trim() && description.trim() && !create.isPending;

  async function handleSubmit() {
    if (!canSubmit) return;
    setError(null);
    try {
      await create.mutateAsync({
        targetTenantId,
        title: title.trim(),
        description: description.trim(),
        category,
        priority,
      });
      onClose();
    } catch {
      setError(t("errorCreateDefault"));
    }
  }

  const inputStyle: React.CSSProperties = {
    width: "100%",
    background: "#111827",
    border: "1px solid #1F2937",
    borderRadius: 8,
    color: "#E8EDF5",
    fontSize: 13,
    padding: "9px 12px",
    outline: "none",
    boxSizing: "border-box",
  };

  const selectStyle: React.CSSProperties = {
    ...inputStyle,
    cursor: "pointer",
  };

  return (
    <div
      style={{
        position: "fixed",
        inset: 0,
        background: "rgba(0,0,0,0.7)",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        zIndex: 50,
      }}
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div
        style={{
          background: "#0D1117",
          border: "1px solid #1F2937",
          borderRadius: 14,
          padding: "24px 28px",
          width: "100%",
          maxWidth: 520,
          display: "flex",
          flexDirection: "column",
          gap: 16,
        }}
      >
        {/* Header */}
        <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between" }}>
          <div style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600 }}>
            {t("createModalTitle")}
          </div>
          <button
            onClick={onClose}
            style={{ background: "none", border: "none", color: "#6B7280", cursor: "pointer" }}
          >
            <X size={18} />
          </button>
        </div>

        {/* Client selector */}
        <div>
          <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
            {t("clientFieldLabel")}
          </label>
          <select
            value={targetTenantId}
            onChange={(e) => setTargetTenantId(e.target.value)}
            style={selectStyle}
          >
            <option value="">{t("selectClientPlaceholder")}</option>
            {activeTenants.map((tenant) => (
              <option key={tenant.id} value={tenant.id}>
                {tenant.name}
              </option>
            ))}
          </select>
        </div>

        {/* Title */}
        <div>
          <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
            {t("titleFieldLabel")}
          </label>
          <input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder={t("titlePlaceholder")}
            style={inputStyle}
          />
        </div>

        {/* Description */}
        <div>
          <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
            {t("descriptionFieldLabel")}
          </label>
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder={t("descriptionPlaceholder")}
            rows={4}
            style={{ ...inputStyle, resize: "vertical", lineHeight: 1.5 }}
          />
        </div>

        {/* Category + Priority */}
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
          <div>
            <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
              {t("metaCategory")}
            </label>
            <select
              value={category}
              onChange={(e) => setCategory(e.target.value as TicketCategory)}
              style={selectStyle}
            >
              {(Object.entries(TICKET_CATEGORY_LABELS) as [TicketCategory, string][]).map(
                ([k, v]) => <option key={k} value={k}>{v}</option>,
              )}
            </select>
          </div>
          <div>
            <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
              {t("metaPriority")}
            </label>
            <select
              value={priority}
              onChange={(e) => setPriority(e.target.value as TicketPriority)}
              style={selectStyle}
            >
              {(Object.entries(TICKET_PRIORITY_LABELS) as [TicketPriority, string][]).map(
                ([k, v]) => <option key={k} value={k}>{v}</option>,
              )}
            </select>
          </div>
        </div>

        {error && (
          <div style={{ color: "#F87171", fontSize: 12 }}>{error}</div>
        )}

        {/* Actions */}
        <div style={{ display: "flex", justifyContent: "flex-end", gap: 10, marginTop: 4 }}>
          <button
            onClick={onClose}
            style={{
              background: "transparent",
              border: "1px solid #1F2937",
              borderRadius: 8,
              color: "#6B7280",
              fontSize: 13,
              padding: "9px 18px",
              cursor: "pointer",
            }}
          >
            {t("cancelButton")}
          </button>
          <Btn onClick={handleSubmit} disabled={!canSubmit}>
            {create.isPending ? t("saving") : t("createButton")}
          </Btn>
        </div>
      </div>
    </div>
  );
}

// ── Main tab component ────────────────────────────────────────────────────────
// STATUS_FILTER_OPTIONS moved inside the component so the "all" option can be
// translated (its TICKET_STATUS_LABELS.* siblings come from the untranslated
// service-desk feature, see the NOTE near formatDate above).

export function ProviderSupportTab() {
  const t = useTranslations("Dashboard.provider.supportTab");
  const STATUS_FILTER_OPTIONS: { value: TicketStatus | ""; label: string }[] = [
    { value: "", label: t("filterAllLabel") },
    { value: "open", label: TICKET_STATUS_LABELS.open },
    { value: "in_progress", label: TICKET_STATUS_LABELS.in_progress },
    { value: "waiting", label: TICKET_STATUS_LABELS.waiting },
    { value: "resolved", label: TICKET_STATUS_LABELS.resolved },
    { value: "closed", label: TICKET_STATUS_LABELS.closed },
  ];

  const [statusFilter, setStatusFilter] = useState<TicketStatus | "">("");
  const [tenantFilter, setTenantFilter] = useState("");
  const [selectedTicketId, setSelectedTicketId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);

  const { data: tickets, isLoading } = useProviderTickets({
    status: statusFilter || undefined,
    tenantId: tenantFilter || undefined,
  });

  const { data: tenants } = useTenants();

  const selectedTicket = tickets?.find((t) => t.id === selectedTicketId) ?? null;

  if (selectedTicket) {
    return (
      <TicketDetailPanel
        ticket={selectedTicket}
        onBack={() => setSelectedTicketId(null)}
      />
    );
  }

  return (
    <div
      style={{
        background: "#0D1117",
        border: "1px solid #1F2937",
        borderRadius: 12,
        padding: "20px 24px",
      }}
    >
      {/* Header */}
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
        <div style={{ display: "flex", alignItems: "center", gap: 8 }}>
          <MessageSquare size={18} color="#60A5FA" />
          <h2 style={{ color: "#E8EDF5", fontSize: 16, fontWeight: 600, margin: 0 }}>
            {t("title")}
          </h2>
          {tickets && (
            <span style={{ color: "#4B5563", fontSize: 12 }}>({tickets.length})</span>
          )}
        </div>

        <Btn icon={<Plus size={14} />} onClick={() => setCreateOpen(true)}>
          {t("newTicketButton")}
        </Btn>
      </div>

      {/* Filters */}
      <div style={{ display: "flex", gap: 8, marginBottom: 16, flexWrap: "wrap" }}>
        {/* Status filter */}
        <div style={{ display: "flex", gap: 6 }}>
          {STATUS_FILTER_OPTIONS.map(({ value, label }) => (
            <button
              key={value}
              onClick={() => setStatusFilter(value)}
              style={{
                padding: "6px 12px",
                borderRadius: 7,
                fontSize: 12,
                cursor: "pointer",
                background: statusFilter === value ? "#1D3461" : "transparent",
                border: `1px solid ${statusFilter === value ? "#3B82F6" : "transparent"}`,
                color: statusFilter === value ? "#93C5FD" : "#6B7280",
              }}
            >
              {label}
            </button>
          ))}
        </div>

        {/* Tenant filter */}
        <select
          value={tenantFilter}
          onChange={(e) => setTenantFilter(e.target.value)}
          style={{
            background: "#111827",
            border: "1px solid #1F2937",
            borderRadius: 7,
            color: "#9CA3AF",
            fontSize: 12,
            padding: "6px 10px",
            cursor: "pointer",
            outline: "none",
          }}
        >
          <option value="">{t("allClientsOption")}</option>
          {(tenants ?? []).map((tenant) => (
            <option key={tenant.id} value={tenant.id}>{tenant.name}</option>
          ))}
        </select>
      </div>

      {/* List */}
      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 14, padding: "20px 0" }}>{t("loading")}</div>
      ) : !tickets?.length ? (
        <div
          style={{
            color: "#4B5563",
            fontSize: 13,
            padding: "40px 0",
            textAlign: "center",
          }}
        >
          {t("empty")}
        </div>
      ) : (
        <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
          {tickets.map((ticket) => (
            <TicketRow
              key={ticket.id}
              ticket={ticket}
              selected={ticket.id === selectedTicketId}
              onClick={() => setSelectedTicketId(ticket.id)}
            />
          ))}
        </div>
      )}

      {createOpen && <CreateTicketModal onClose={() => setCreateOpen(false)} />}
    </div>
  );
}
