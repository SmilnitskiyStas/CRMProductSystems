"use client";

import { useState } from "react";
import { MessageSquare, Plus, ChevronLeft, Building2, User, X } from "lucide-react";
import { useProviderTickets, useCreateProviderTicket } from "@/features/service-desk/hooks/useProviderTickets";
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

// ── Helpers ──────────────────────────────────────────────────────────────────

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("uk-UA", {
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
            Провайдер
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
        <span style={{ color: "#4B5563", fontSize: 12 }}>{formatDate(ticket.createdAt)}</span>
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

function TicketDetailPanel({
  ticket,
  onBack,
}: {
  ticket: ProviderTicketListItemDto;
  onBack: () => void;
}) {
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
        {ticket.description || <span style={{ color: "#374151" }}>Опис відсутній</span>}
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
          ["Категорія",  TICKET_CATEGORY_LABELS[ticket.category]],
          ["Статус",     TICKET_STATUS_LABELS[ticket.status]],
          ["Пріоритет",  TICKET_PRIORITY_LABELS[ticket.priority]],
          ["Автор",      ticket.createdByName],
          ["Компанія",   ticket.tenantName],
          ["Створено",   formatDate(ticket.createdAt)],
          ["Коментарів", String(ticket.commentCount)],
          ["Ініціатор",  ticket.createdByProvider ? "Провайдер" : "Клієнт"],
        ].map(([label, value]) => (
          <div key={label}>
            <div style={{ color: "#4B5563", fontSize: 11, marginBottom: 2 }}>{label}</div>
            <div style={{ color: "#E8EDF5", fontSize: 13 }}>{value}</div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Create ticket modal ───────────────────────────────────────────────────────

function CreateTicketModal({ onClose }: { onClose: () => void }) {
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
      setError("Помилка при створенні тікета. Спробуйте ще раз.");
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
            Новий тікет для клієнта
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
            Клієнт *
          </label>
          <select
            value={targetTenantId}
            onChange={(e) => setTargetTenantId(e.target.value)}
            style={selectStyle}
          >
            <option value="">Оберіть клієнта…</option>
            {activeTenants.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        </div>

        {/* Title */}
        <div>
          <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
            Тема *
          </label>
          <input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Коротко опишіть проблему"
            style={inputStyle}
          />
        </div>

        {/* Description */}
        <div>
          <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
            Опис *
          </label>
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Детальний опис завдання або проблеми"
            rows={4}
            style={{ ...inputStyle, resize: "vertical", lineHeight: 1.5 }}
          />
        </div>

        {/* Category + Priority */}
        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 12 }}>
          <div>
            <label style={{ color: "#9CA3AF", fontSize: 12, marginBottom: 6, display: "block" }}>
              Категорія
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
              Пріоритет
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
            Скасувати
          </button>
          <button
            onClick={handleSubmit}
            disabled={!canSubmit}
            style={{
              background: canSubmit ? "#1D3461" : "#111827",
              border: `1px solid ${canSubmit ? "#3B82F6" : "#1F2937"}`,
              borderRadius: 8,
              color: canSubmit ? "#93C5FD" : "#374151",
              fontSize: 13,
              fontWeight: 600,
              padding: "9px 18px",
              cursor: canSubmit ? "pointer" : "not-allowed",
            }}
          >
            {create.isPending ? "Збереження…" : "Створити тікет"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Main tab component ────────────────────────────────────────────────────────

const STATUS_FILTER_OPTIONS: { value: TicketStatus | ""; label: string }[] = [
  { value: "", label: "Всі" },
  { value: "open", label: TICKET_STATUS_LABELS.open },
  { value: "in_progress", label: TICKET_STATUS_LABELS.in_progress },
  { value: "waiting", label: TICKET_STATUS_LABELS.waiting },
  { value: "resolved", label: TICKET_STATUS_LABELS.resolved },
  { value: "closed", label: TICKET_STATUS_LABELS.closed },
];

export function ProviderSupportTab() {
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
            Тікети підтримки
          </h2>
          {tickets && (
            <span style={{ color: "#4B5563", fontSize: 12 }}>({tickets.length})</span>
          )}
        </div>

        <button
          onClick={() => setCreateOpen(true)}
          style={{
            display: "flex",
            alignItems: "center",
            gap: 7,
            background: "#1D3461",
            border: "1px solid #3B82F6",
            borderRadius: 8,
            padding: "8px 14px",
            color: "#93C5FD",
            fontSize: 13,
            fontWeight: 600,
            cursor: "pointer",
          }}
        >
          <Plus size={14} />
          Новий тікет
        </button>
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
          <option value="">Всі клієнти</option>
          {(tenants ?? []).map((t) => (
            <option key={t.id} value={t.id}>{t.name}</option>
          ))}
        </select>
      </div>

      {/* List */}
      {isLoading ? (
        <div style={{ color: "#4B5563", fontSize: 14, padding: "20px 0" }}>Завантаження…</div>
      ) : !tickets?.length ? (
        <div
          style={{
            color: "#4B5563",
            fontSize: 13,
            padding: "40px 0",
            textAlign: "center",
          }}
        >
          Тікетів немає
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
